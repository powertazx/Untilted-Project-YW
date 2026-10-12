using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace YoKaiWatchLauncher
{
    public class PlayerSlot
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

        [JsonPropertyName("name")]
        public string Name { get; set; } = "Joueur";

        [JsonPropertyName("characterId")]
        public string CharacterId { get; set; } = "";

        [JsonPropertyName("characterName")]
        public string CharacterName { get; set; } = "";

        [JsonPropertyName("isHost")]
        public bool IsHost { get; set; }

        [JsonPropertyName("isReady")]
        public bool IsReady { get; set; }

        [JsonPropertyName("ping")]
        public int Ping { get; set; } = 15;

        [JsonPropertyName("endpoint")]
        public string Endpoint { get; set; } = "";

        [JsonPropertyName("x")]
        public float X { get; set; }

        [JsonPropertyName("y")]
        public float Y { get; set; }

        [JsonPropertyName("z")]
        public float Z { get; set; }

        [JsonPropertyName("lastUpdate")]
        public DateTime LastUpdate { get; set; } = DateTime.Now;
    }

    public class LobbyRoom
    {
        [JsonPropertyName("code")]
        public string Code { get; set; } = "";

        [JsonPropertyName("hostName")]
        public string HostName { get; set; } = "";

        [JsonPropertyName("players")]
        public List<PlayerSlot> Players { get; set; } = new();

        [JsonPropertyName("lockedCharacters")]
        public List<string> LockedCharacters { get; set; } = new();

        [JsonPropertyName("isGameStarted")]
        public bool IsGameStarted { get; set; }

        [JsonPropertyName("maxPlayers")]
        public int MaxPlayers { get; set; } = 4;

        [JsonPropertyName("hostIp")]
        public string HostIp { get; set; } = "127.0.0.1";

        [JsonPropertyName("port")]
        public int Port { get; set; } = 7777;
    }

    public class NetworkMessage
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = ""; // JOIN, JOIN_ACK, SELECT_CHAR, READY, CHAT, START_GAME, STATE, ERROR

        [JsonPropertyName("code")]
        public string Code { get; set; } = "";

        [JsonPropertyName("senderId")]
        public string SenderId { get; set; } = "";

        [JsonPropertyName("senderName")]
        public string SenderName { get; set; } = "";

        [JsonPropertyName("characterId")]
        public string CharacterId { get; set; } = "";

        [JsonPropertyName("message")]
        public string Message { get; set; } = "";

        [JsonPropertyName("room")]
        public LobbyRoom? Room { get; set; }
    }

    public class MultiplayerSessionConfig
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonPropertyName("room_code")]
        public string RoomCode { get; set; } = "";

        [JsonPropertyName("is_host")]
        public bool IsHost { get; set; }

        [JsonPropertyName("player_id")]
        public string PlayerId { get; set; } = "";

        [JsonPropertyName("player_name")]
        public string PlayerName { get; set; } = "";

        [JsonPropertyName("character_id")]
        public string CharacterId { get; set; } = "";

        [JsonPropertyName("character_name")]
        public string CharacterName { get; set; } = "";

        [JsonPropertyName("server_address")]
        public string ServerAddress { get; set; } = "127.0.0.1";

        [JsonPropertyName("udp_port")]
        public int UdpPort { get; set; } = 7778;

        [JsonPropertyName("sync_interval_ms")]
        public int SyncIntervalMs { get; set; } = 33; // 30 FPS

        [JsonPropertyName("players")]
        public List<PlayerSlot> Players { get; set; } = new();
    }
}
