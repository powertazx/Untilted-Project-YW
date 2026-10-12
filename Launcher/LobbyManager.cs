using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace YoKaiWatchLauncher
{
    public class ConnectedClient : IDisposable
    {
        public string PlayerId { get; set; } = "";
        public TcpClient TcpClient { get; }
        public NetworkStream Stream { get; }
        public StreamReader Reader { get; }
        public StreamWriter Writer { get; }
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private bool _disposed;

        public ConnectedClient(TcpClient tcpClient, UTF8Encoding encoding)
        {
            TcpClient = tcpClient;
            Stream = tcpClient.GetStream();
            Reader = new StreamReader(Stream, encoding, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
            Writer = new StreamWriter(Stream, encoding, bufferSize: 4096, leaveOpen: true) { AutoFlush = true };
        }

        public async Task SendAsync(string json)
        {
            if (_disposed || !TcpClient.Connected) return;
            await _sendLock.WaitAsync();
            try
            {
                if (!_disposed && TcpClient.Connected)
                {
                    await Writer.WriteLineAsync(json);
                }
            }
            catch { }
            finally
            {
                try { _sendLock.Release(); } catch { }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { Reader.Dispose(); } catch { }
            try { Writer.Dispose(); } catch { }
            try { Stream.Dispose(); } catch { }
            try { TcpClient.Close(); } catch { }
            try { _sendLock.Dispose(); } catch { }
        }
    }

    public class LobbyManager
    {
        public static LobbyManager Instance { get; } = new();

        public LobbyRoom? CurrentRoom { get; private set; }
        public PlayerSlot? LocalPlayer { get; private set; }
        public bool IsHost { get; private set; }
        public string? ConnectedHostAddress { get; private set; }

        private TcpListener? _tcpListener;
        private TcpClient? _clientSocket;
        private NetworkStream? _clientStream;
        private StreamReader? _clientReader;
        private StreamWriter? _clientWriter;
        private readonly SemaphoreSlim _clientSendLock = new(1, 1);

        private CancellationTokenSource? _cts;
        private UdpClient? _udpBeacon;
        private UdpClient? _udpPositionRelay;

        private readonly ConcurrentDictionary<string, ConnectedClient> _connectedClients = new();
        private readonly ConcurrentDictionary<string, IPEndPoint> _udpEndpoints = new();

        public event Action<LobbyRoom?>? RoomUpdated;
        public event Action<string>? LogMessageReceived;
        public event Action<string>? GameStartTriggered;

        private static readonly Random _rng = new();
        private const string CodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // readable chars without 0/O 1/I
        private static readonly UTF8Encoding Utf8NoBom = new(false);

        public static readonly (string Id, string Name)[] DefaultCharacters = new[]
        {
            ("nathan", "Nathan Adams"),
            ("kathie", "Kathie Forester"),
            ("wyatt", "Wyatt"),
            ("nathaniel", "Nathaniel Adams"),
            ("michael", "Michael Forester"),
            ("lucas", "Lucas"),
            ("balaize", "Balaize"),
            ("matt", "Matt")
        };

        public static string GetCharacterName(string id)
        {
            foreach (var c in DefaultCharacters)
            {
                if (c.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) return c.Name;
            }
            return "Nathan Adams";
        }

        public static string GetFirstAvailableCharacter(LobbyRoom room)
        {
            var taken = new HashSet<string>(room.Players.Select(p => p.CharacterId), StringComparer.OrdinalIgnoreCase);
            foreach (var c in DefaultCharacters)
            {
                if (!taken.Contains(c.Id)) return c.Id;
            }
            return "nathan";
        }

        public static string GetPrimaryLanIp()
        {
            try
            {
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
                socket.Connect("8.8.8.8", 65530);
                if (socket.LocalEndPoint is IPEndPoint ep && !IPAddress.IsLoopback(ep.Address))
                {
                    return ep.Address.ToString();
                }
            }
            catch { }

            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(ip) &&
                        !ip.ToString().StartsWith("169.254"))
                    {
                        return ip.ToString();
                    }
                }
            }
            catch { }

            return "127.0.0.1";
        }

        public static string GenerateRoomCode()
        {
            char[] code = new char[5];
            for (int i = 0; i < 5; i++)
            {
                code[i] = CodeChars[_rng.Next(CodeChars.Length)];
            }
            return new string(code);
        }

        public Task<string> CreateRoomAsync(string playerName, string characterId, string characterName, int port = 7777)
        {
            LeaveRoom(notifyUi: false);
            _cts = new CancellationTokenSource();

            string code = GenerateRoomCode();
            IsHost = true;
            ConnectedHostAddress = "127.0.0.1";
            string lanIp = GetPrimaryLanIp();

            LocalPlayer = new PlayerSlot
            {
                Id = Guid.NewGuid().ToString("N")[..8],
                Name = string.IsNullOrWhiteSpace(playerName) ? "Hôte" : playerName.Trim(),
                CharacterId = string.IsNullOrEmpty(characterId) ? "nathan" : characterId,
                CharacterName = string.IsNullOrEmpty(characterName) ? GetCharacterName(characterId) : characterName,
                IsHost = true,
                IsReady = true
            };

            CurrentRoom = new LobbyRoom
            {
                Code = code,
                HostName = LocalPlayer.Name,
                Players = new List<PlayerSlot> { LocalPlayer },
                LockedCharacters = new List<string> { LocalPlayer.CharacterId },
                HostIp = lanIp,
                Port = port
            };

            try
            {
                _tcpListener = new TcpListener(IPAddress.Any, port);
                _tcpListener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _tcpListener.Start();
                LogMessageReceived?.Invoke($"[Lobby] Salon {code} créé sur {lanIp}:{port}. En attente de joueurs...");

                // Start UDP position relay
                StartUdpRelay(port + 1);

                // Start accepting clients in background
                _ = Task.Run(() => AcceptClientsLoopAsync(_cts.Token));

                // Start LAN discovery beacon
                StartUdpBeacon(code, LocalPlayer.Name, lanIp, port, _cts.Token);

                RoomUpdated?.Invoke(CurrentRoom);
                return Task.FromResult(code);
            }
            catch (Exception ex)
            {
                LogMessageReceived?.Invoke($"[Erreur Lobby] Impossible de démarrer le salon : {ex.Message}");
                LeaveRoom(notifyUi: true);
                throw;
            }
        }

        private async Task AcceptClientsLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _tcpListener != null)
            {
                try
                {
                    var client = await _tcpListener.AcceptTcpClientAsync(token);
                    client.NoDelay = true;
                    _ = Task.Run(() => HandleClientAsync(client, token), token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (token.IsCancellationRequested) break;
                    LogMessageReceived?.Invoke($"[Lobby] Erreur accept client : {ex.Message}");
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken token)
        {
            var clientConn = new ConnectedClient(client, Utf8NoBom);
            string? playerId = null;

            try
            {
                while (!token.IsCancellationRequested && clientConn.TcpClient.Connected)
                {
                    string? line = await clientConn.Reader.ReadLineAsync(token);
                    if (line == null) break;

                    var msg = JsonSerializer.Deserialize<NetworkMessage>(line);
                    if (msg == null) continue;

                    if (msg.Type == "JOIN")
                    {
                        if (CurrentRoom == null || !string.Equals(msg.Code, CurrentRoom.Code, StringComparison.OrdinalIgnoreCase))
                        {
                            var reject = new NetworkMessage { Type = "ERROR", Message = "Code de salon invalide." };
                            await clientConn.SendAsync(JsonSerializer.Serialize(reject));
                            clientConn.Dispose();
                            return;
                        }

                        if (CurrentRoom.Players.Count >= CurrentRoom.MaxPlayers)
                        {
                            var reject = new NetworkMessage { Type = "ERROR", Message = "Le salon est plein (4/4 joueurs)." };
                            await clientConn.SendAsync(JsonSerializer.Serialize(reject));
                            clientConn.Dispose();
                            return;
                        }

                        string requestedName = string.IsNullOrWhiteSpace(msg.SenderName) ? $"Joueur {CurrentRoom.Players.Count + 1}" : msg.SenderName.Trim();
                        bool nameTaken;
                        lock (CurrentRoom)
                        {
                            nameTaken = CurrentRoom.Players.Any(p => p.Name.Equals(requestedName, StringComparison.OrdinalIgnoreCase));
                        }

                        if (nameTaken)
                        {
                            var reject = new NetworkMessage
                            {
                                Type = "ERROR",
                                Message = $"Le pseudo \"{requestedName}\" est déjà utilisé par un joueur dans ce salon. Veuillez en choisir un autre."
                            };
                            await clientConn.SendAsync(JsonSerializer.Serialize(reject));
                            clientConn.Dispose();
                            return;
                        }

                        playerId = msg.SenderId;
                        clientConn.PlayerId = playerId;
                        _connectedClients[playerId] = clientConn;

                        // Check character conflict: if requested character already taken, pick next free
                        string chosenChar = msg.CharacterId;
                        lock (CurrentRoom)
                        {
                            if (string.IsNullOrEmpty(chosenChar) || CurrentRoom.Players.Any(p => p.CharacterId.Equals(chosenChar, StringComparison.OrdinalIgnoreCase)))
                            {
                                chosenChar = GetFirstAvailableCharacter(CurrentRoom);
                            }

                            var newPlayer = new PlayerSlot
                            {
                                Id = playerId,
                                Name = requestedName,
                                CharacterId = chosenChar,
                                CharacterName = GetCharacterName(chosenChar),
                                IsHost = false,
                                IsReady = false
                            };

                            CurrentRoom.Players.Add(newPlayer);
                            UpdateLockedCharacters();
                            LogMessageReceived?.Invoke($"[Lobby] {newPlayer.Name} a rejoint le salon avec le personnage {newPlayer.CharacterName} !");
                        }

                        // Broadcast room state to all clients (including this client as its initial response)
                        await BroadcastRoomStateAsync();
                    }
                    else if (msg.Type == "SELECT_CHAR" && playerId != null && CurrentRoom != null)
                    {
                        lock (CurrentRoom)
                        {
                            var player = CurrentRoom.Players.FirstOrDefault(p => p.Id == playerId);
                            if (player != null)
                            {
                                bool taken = CurrentRoom.Players.Any(p => p.Id != playerId && p.CharacterId.Equals(msg.CharacterId, StringComparison.OrdinalIgnoreCase));
                                if (!taken)
                                {
                                    player.CharacterId = msg.CharacterId;
                                    player.CharacterName = string.IsNullOrWhiteSpace(msg.Message) ? GetCharacterName(msg.CharacterId) : msg.Message;
                                    UpdateLockedCharacters();
                                    LogMessageReceived?.Invoke($"[Lobby] {player.Name} a choisi {player.CharacterName}.");
                                }
                            }
                        }
                        await BroadcastRoomStateAsync();
                    }
                    else if (msg.Type == "READY" && playerId != null && CurrentRoom != null)
                    {
                        lock (CurrentRoom)
                        {
                            var player = CurrentRoom.Players.FirstOrDefault(p => p.Id == playerId);
                            if (player != null)
                            {
                                player.IsReady = !player.IsReady;
                            }
                        }
                        await BroadcastRoomStateAsync();
                    }
                    else if (msg.Type == "CHAT")
                    {
                        await BroadcastMessageAsync(msg);
                    }
                }
            }
            catch (Exception)
            {
                // Disconnected
            }
            finally
            {
                if (playerId != null)
                {
                    _connectedClients.TryRemove(playerId, out var c);
                    c?.Dispose();

                    if (CurrentRoom != null)
                    {
                        lock (CurrentRoom)
                        {
                            var left = CurrentRoom.Players.FirstOrDefault(p => p.Id == playerId);
                            if (left != null)
                            {
                                CurrentRoom.Players.Remove(left);
                                UpdateLockedCharacters();
                                LogMessageReceived?.Invoke($"[Lobby] {left.Name} a quitté le salon.");
                            }
                        }
                        _ = BroadcastRoomStateAsync();
                    }
                }
            }
        }

        private void UpdateLockedCharacters()
        {
            if (CurrentRoom == null) return;
            CurrentRoom.LockedCharacters = CurrentRoom.Players
                .Where(p => !string.IsNullOrEmpty(p.CharacterId))
                .Select(p => p.CharacterId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private async Task BroadcastRoomStateAsync()
        {
            if (CurrentRoom == null) return;

            CurrentRoom.HostIp = GetPrimaryLanIp();

            var msg = new NetworkMessage
            {
                Type = "STATE",
                Code = CurrentRoom.Code,
                Room = CurrentRoom
            };

            await BroadcastMessageAsync(msg);
            RoomUpdated?.Invoke(CurrentRoom);
        }

        private async Task BroadcastMessageAsync(NetworkMessage msg)
        {
            string json = JsonSerializer.Serialize(msg);
            var clients = _connectedClients.Values.ToArray();
            foreach (var client in clients)
            {
                try
                {
                    await client.SendAsync(json);
                }
                catch { }
            }
        }

        private static async Task<bool> ProbeTcpPortAsync(string host, int port, int timeoutMs = 200)
        {
            try
            {
                using var client = new TcpClient();
                using var cts = new CancellationTokenSource(timeoutMs);
                await client.ConnectAsync(host, port, cts.Token);
                return client.Connected;
            }
            catch { }
            return false;
        }

        public async Task<(bool Success, string Message, LobbyRoom? Room)> JoinRoomAsync(
            string code, string playerName, string characterId, string characterName, string? targetIp = null, int port = 7777)
        {
            LeaveRoom(notifyUi: false);
            _cts = new CancellationTokenSource();

            code = code.Trim().ToUpperInvariant();
            IsHost = false;

            string ipToConnect = targetIp?.Trim() ?? "";

            // If no IP specified or AUTO, first test if a host is running locally (same PC)
            if (string.IsNullOrEmpty(ipToConnect) || ipToConnect.Equals("AUTO", StringComparison.OrdinalIgnoreCase))
            {
                bool isLocalHost = await ProbeTcpPortAsync("127.0.0.1", port, 200);
                if (isLocalHost)
                {
                    ipToConnect = "127.0.0.1";
                }
                else
                {
                    LogMessageReceived?.Invoke($"[Lobby] Recherche du salon {code} sur le réseau local...");
                    ipToConnect = await DiscoverHostOnLanAsync(code, 2500) ?? "127.0.0.1";
                }
            }

            try
            {
                _clientSocket = new TcpClient();
                _clientSocket.NoDelay = true;
                await _clientSocket.ConnectAsync(ipToConnect, port);
                _clientStream = _clientSocket.GetStream();
                _clientReader = new StreamReader(_clientStream, Utf8NoBom, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
                _clientWriter = new StreamWriter(_clientStream, Utf8NoBom, bufferSize: 4096, leaveOpen: true) { AutoFlush = true };

                LocalPlayer = new PlayerSlot
                {
                    Id = Guid.NewGuid().ToString("N")[..8],
                    Name = string.IsNullOrWhiteSpace(playerName) ? "Joueur" : playerName.Trim(),
                    CharacterId = string.IsNullOrEmpty(characterId) ? "nathan" : characterId,
                    CharacterName = string.IsNullOrEmpty(characterName) ? GetCharacterName(characterId) : characterName,
                    IsHost = false,
                    IsReady = false
                };

                var joinMsg = new NetworkMessage
                {
                    Type = "JOIN",
                    Code = code,
                    SenderId = LocalPlayer.Id,
                    SenderName = LocalPlayer.Name,
                    CharacterId = LocalPlayer.CharacterId
                };

                await _clientSendLock.WaitAsync();
                try
                {
                    await _clientWriter.WriteLineAsync(JsonSerializer.Serialize(joinMsg));
                }
                finally
                {
                    _clientSendLock.Release();
                }

                // Wait for initial handshake response from host
                using var ctsHandshake = new CancellationTokenSource(4000);
                string? firstLine = await _clientReader.ReadLineAsync(ctsHandshake.Token);
                if (string.IsNullOrEmpty(firstLine))
                {
                    throw new Exception("Le salon n'a pas répondu ou a fermé la connexion.");
                }

                var firstMsg = JsonSerializer.Deserialize<NetworkMessage>(firstLine);
                if (firstMsg == null)
                {
                    throw new Exception("Réponse invalide du serveur hôte.");
                }

                if (firstMsg.Type == "ERROR")
                {
                    throw new Exception(firstMsg.Message ?? "Connexion refusée par l'hôte.");
                }

                if (firstMsg.Type == "STATE" && firstMsg.Room != null)
                {
                    CurrentRoom = firstMsg.Room;

                    // Sync local character if host re-assigned it due to conflict
                    var mySlot = CurrentRoom.Players.FirstOrDefault(p => p.Id == LocalPlayer.Id);
                    if (mySlot != null)
                    {
                        LocalPlayer.CharacterId = mySlot.CharacterId;
                        LocalPlayer.CharacterName = mySlot.CharacterName;
                    }
                }
                else
                {
                    throw new Exception("Réponse inattendue lors de la connexion.");
                }

                ConnectedHostAddress = ipToConnect;

                // Start persistent receive loop
                _ = Task.Run(() => ClientReceiveLoopAsync(_cts.Token));

                LogMessageReceived?.Invoke($"[Lobby] Connecté avec succès au salon {code} ({ipToConnect}:{port}) !");
                RoomUpdated?.Invoke(CurrentRoom);
                return (true, "", CurrentRoom);
            }
            catch (Exception ex)
            {
                LogMessageReceived?.Invoke($"[Erreur Connexion] Impossible de rejoindre le salon : {ex.Message}");
                LeaveRoom(notifyUi: false);
                return (false, ex.Message, null);
            }
        }

        private async Task ClientReceiveLoopAsync(CancellationToken token)
        {
            if (_clientReader == null) return;

            try
            {
                while (!token.IsCancellationRequested && _clientSocket != null && _clientSocket.Connected)
                {
                    string? line = await _clientReader.ReadLineAsync(token);
                    if (line == null) break;

                    var msg = JsonSerializer.Deserialize<NetworkMessage>(line);
                    if (msg == null) continue;

                    if (msg.Type == "STATE" && msg.Room != null)
                    {
                        CurrentRoom = msg.Room;

                        var mySlot = CurrentRoom.Players.FirstOrDefault(p => p.Id == LocalPlayer?.Id);
                        if (mySlot != null && LocalPlayer != null)
                        {
                            LocalPlayer.CharacterId = mySlot.CharacterId;
                            LocalPlayer.CharacterName = mySlot.CharacterName;
                        }

                        RoomUpdated?.Invoke(CurrentRoom);
                    }
                    else if (msg.Type == "START_GAME")
                    {
                        LogMessageReceived?.Invoke("[Lobby] L'hôte a lancé la session ! Démarrage du jeu...");
                        GameStartTriggered?.Invoke(msg.Code);
                    }
                    else if (msg.Type == "ERROR")
                    {
                        LogMessageReceived?.Invoke($"[Erreur Salon] {msg.Message}");
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    LogMessageReceived?.Invoke($"[Lobby] Connexion interrompue : {ex.Message}");
                }
            }
            finally
            {
                if (!token.IsCancellationRequested && CurrentRoom != null)
                {
                    LogMessageReceived?.Invoke("[Lobby] Déconnecté du salon.");
                    LeaveRoom(notifyUi: true);
                }
            }
        }

        public async Task<bool> SelectCharacterAsync(string charId, string charName)
        {
            if (LocalPlayer == null || CurrentRoom == null) return false;

            // Check if another player in room already has this character
            bool taken = CurrentRoom.Players.Any(p => p.Id != LocalPlayer.Id && p.CharacterId.Equals(charId, StringComparison.OrdinalIgnoreCase));
            if (taken)
            {
                LogMessageReceived?.Invoke($"[Attention] Ce personnage est déjà choisi par un autre joueur !");
                return false;
            }

            LocalPlayer.CharacterId = charId;
            LocalPlayer.CharacterName = charName;

            if (IsHost)
            {
                lock (CurrentRoom)
                {
                    var p = CurrentRoom.Players.FirstOrDefault(x => x.Id == LocalPlayer.Id);
                    if (p != null)
                    {
                        p.CharacterId = charId;
                        p.CharacterName = charName;
                    }
                    UpdateLockedCharacters();
                }
                await BroadcastRoomStateAsync();
                return true;
            }
            else
            {
                var msg = new NetworkMessage
                {
                    Type = "SELECT_CHAR",
                    SenderId = LocalPlayer.Id,
                    CharacterId = charId,
                    Message = charName
                };
                await SendClientMessageAsync(msg);
                return true;
            }
        }

        private async Task SendClientMessageAsync(NetworkMessage msg)
        {
            if (_clientWriter == null || _clientSocket == null || !_clientSocket.Connected) return;
            await _clientSendLock.WaitAsync();
            try
            {
                string json = JsonSerializer.Serialize(msg);
                await _clientWriter.WriteLineAsync(json);
            }
            catch { }
            finally
            {
                try { _clientSendLock.Release(); } catch { }
            }
        }

        public void StartGameSession()
        {
            if (!IsHost || CurrentRoom == null) return;

            CurrentRoom.IsGameStarted = true;
            _ = BroadcastMessageAsync(new NetworkMessage
            {
                Type = "START_GAME",
                Code = CurrentRoom.Code
            });

            GameStartTriggered?.Invoke(CurrentRoom.Code);
        }

        public void LeaveRoom(bool notifyUi = true)
        {
            try { _cts?.Cancel(); } catch { }
            _cts = null;

            try { _tcpListener?.Stop(); } catch { }
            _tcpListener = null;

            foreach (var kvp in _connectedClients)
            {
                try { kvp.Value.Dispose(); } catch { }
            }
            _connectedClients.Clear();

            try { _clientWriter?.Dispose(); } catch { }
            _clientWriter = null;
            try { _clientReader?.Dispose(); } catch { }
            _clientReader = null;
            try { _clientStream?.Dispose(); } catch { }
            _clientStream = null;
            try { _clientSocket?.Close(); } catch { }
            _clientSocket = null;

            try { _udpBeacon?.Close(); } catch { }
            _udpBeacon = null;
            try { _udpPositionRelay?.Close(); } catch { }
            _udpPositionRelay = null;
            _udpEndpoints.Clear();

            CurrentRoom = null;
            LocalPlayer = null;
            IsHost = false;
            ConnectedHostAddress = null;

            if (notifyUi)
            {
                RoomUpdated?.Invoke(null);
            }
        }

        #region UDP LAN Discovery & Position Relay
        private void StartUdpBeacon(string roomCode, string hostName, string hostIp, int port, CancellationToken token)
        {
            try
            {
                _udpBeacon = new UdpClient { EnableBroadcast = true };
                _ = Task.Run(async () =>
                {
                    byte[] data = Utf8NoBom.GetBytes(JsonSerializer.Serialize(new
                    {
                        code = roomCode,
                        host = hostName,
                        ip = hostIp,
                        port
                    }));
                    var endpoint = new IPEndPoint(IPAddress.Broadcast, 7779);

                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            await _udpBeacon.SendAsync(data, data.Length, endpoint);
                        }
                        catch { }
                        await Task.Delay(1000, token);
                    }
                }, token);
            }
            catch { }
        }

        private async Task<string?> DiscoverHostOnLanAsync(string targetCode, int timeoutMs = 2500)
        {
            try
            {
                using var udp = new UdpClient();
                udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udp.Client.Bind(new IPEndPoint(IPAddress.Any, 7779));

                using var cts = new CancellationTokenSource(timeoutMs);
                while (!cts.IsCancellationRequested)
                {
                    var result = await udp.ReceiveAsync(cts.Token);
                    string json = Utf8NoBom.GetString(result.Buffer);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("code", out var codeElem) &&
                        string.Equals(codeElem.GetString(), targetCode, StringComparison.OrdinalIgnoreCase))
                    {
                        if (doc.RootElement.TryGetProperty("ip", out var ipElem) && !string.IsNullOrEmpty(ipElem.GetString()))
                        {
                            return ipElem.GetString();
                        }
                        return result.RemoteEndPoint.Address.ToString();
                    }
                }
            }
            catch { }
            return null;
        }

        private void StartUdpRelay(int udpPort)
        {
            try
            {
                _udpPositionRelay = new UdpClient();
                _udpPositionRelay.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udpPositionRelay.Client.Bind(new IPEndPoint(IPAddress.Any, udpPort));

                _ = Task.Run(async () =>
                {
                    while (_cts != null && !_cts.IsCancellationRequested && _udpPositionRelay != null)
                    {
                        try
                        {
                            var result = await _udpPositionRelay.ReceiveAsync();
                            _udpEndpoints[result.RemoteEndPoint.ToString()] = result.RemoteEndPoint;

                            foreach (var ep in _udpEndpoints.Values)
                            {
                                if (!ep.Equals(result.RemoteEndPoint))
                                {
                                    try
                                    {
                                        await _udpPositionRelay.SendAsync(result.Buffer, result.Buffer.Length, ep);
                                    }
                                    catch { }
                                }
                            }
                        }
                        catch { }
                    }
                });
            }
            catch { }
        }
        #endregion
    }
}
