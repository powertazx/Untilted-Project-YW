// ==========================================================================
// YO-KAI WATCH PC LAUNCHER - JAVASCRIPT APP
// ==========================================================================

const CHARACTERS = [
    {
        id: "nathan",
        name: "Nathan Adams",
        jp: "Keita Amano",
        initial: "N",
        color: "#3b82f6",
        title: "Le Maître Yo-kai",
        watch: "Yo-kai Watch Modèle U",
        bio: "Le héros de Granval-sur-Mer. Grâce à sa Yo-kai Watch, il peut voir et invoquer ses amis Yo-kai."
    },
    {
        id: "kathie",
        name: "Kathie Forester",
        jp: "Fumika Kodama",
        initial: "K",
        color: "#ec4899",
        title: "L'Aventurière Intrépide",
        watch: "Yo-kai Watch Pendentif",
        bio: "L'amie d'enfance de Nathan, curieuse et courageuse, dotée d'une grande intuition spirituelle."
    },
    {
        id: "wyatt",
        name: "Wyatt",
        jp: "Mac Hazeltine",
        initial: "W",
        color: "#f59e0b",
        title: "Le Détective Américain",
        watch: "Yo-kai Watch Modèle U USA",
        bio: "Le jeune aventurier de Pistachia / St. Peanutsburg aux États-Unis, allié de Nathan."
    },
    {
        id: "nathaniel",
        name: "Nathaniel Adams",
        jp: "Keizo Amano",
        initial: "NA",
        color: "#ef4444",
        title: "Le Créateur de la Watch",
        watch: "Yo-kai Watch Modèle Zéro",
        bio: "Le grand-père de Nathan. Courageux, audacieux et créateur du prototype de la Yo-kai Watch."
    },
    {
        id: "michael",
        name: "Michael Forester",
        jp: "Yukippe",
        initial: "MF",
        color: "#10b981",
        title: "L'Ami du Passé",
        watch: "Montre Traditionnelle",
        bio: "Le grand-père de Kathie autrefois à Nostalgiville, plein de sagesse et de complicité."
    },
    {
        id: "lucas",
        name: "Lucas",
        jp: "Lord Enma / Lucas",
        initial: "L",
        color: "#8b5cf6",
        title: "Gardien de la Clé du Monde Yo-kai",
        watch: "Clé du Royaume des Yo-kai",
        bio: "L'énigmatique garçon aux yeux d'or qui détenait la clé menant au Royaume des Yo-kai dans le 1."
    },
    {
        id: "balaize",
        name: "Balaize",
        jp: "Gorita / Bear",
        initial: "B",
        color: "#ea580c",
        title: "Le Protecteur Robuste",
        watch: "Bandeau & Énergie",
        bio: "Le camarade fidèle et robuste de l'école élémentaire, toujours prêt à défendre ses amis."
    },
    {
        id: "matt",
        name: "Matt",
        jp: "Eddie / Kanchi",
        initial: "M",
        color: "#06b6d4",
        title: "L'Expert High-Tech",
        watch: "Casque & Gadgets",
        bio: "Le petit génie branché de la bande, féru d'électronique et d'objets connectés."
    }
];

// App State
let state = {
    playerName: localStorage.getItem("ykw_player_name") || "Nathan",
    selectedCharacterId: "nathan",
    inRoom: false,
    isHost: false,
    currentRoom: null,
    localPlayerId: null,
    localInGame: false,
    settings: null,
    rebindAction: null,
    rebindType: null // "keys" or "pad"
};

// C# Bridge accessor
function getBridge() {
    if (window.chrome && window.chrome.webview && window.chrome.webview.hostObjects && window.chrome.webview.hostObjects.launcherBackend) {
        return window.chrome.webview.hostObjects.launcherBackend;
    }
    return null;
}

// Toast
function showToast(msg) {
    const toast = document.getElementById("toast-notify");
    if (!toast) return;
    toast.textContent = msg;
    toast.classList.add("show");
    setTimeout(() => toast.classList.remove("show"), 2800);
}

// ==========================================
// SUPABASE AUTO-UPDATE SYSTEM
// ==========================================
const CURRENT_LAUNCHER_VERSION = "1.0.0";
const SUPABASE_PROJECT_URL = "https://edjolpaagijjqjvhiibw.supabase.co";
const SUPABASE_ANON_KEY = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImVkam9scGFhZ2lqanFqdmhpaWJ3Iiwicm9sZSI6ImFub24iLCJpYXQiOjE3OTE3Mzk4MjUsImV4cCI6MjEwNzMxNTgyNX0.R1AInn-bPruBM9sey73mzxqkzcQz9wi3ApUwdD6y7jU";

function compareSemVer(vA, vB) {
    const cleanA = vA.replace(/^v/i, "").split(".").map(n => parseInt(n) || 0);
    const cleanB = vB.replace(/^v/i, "").split(".").map(n => parseInt(n) || 0);
    for (let i = 0; i < Math.max(cleanA.length, cleanB.length); i++) {
        const valA = cleanA[i] || 0;
        const valB = cleanB[i] || 0;
        if (valA > valB) return 1;
        if (valA < valB) return -1;
    }
    return 0;
}

function formatDateFr(isoDateString) {
    if (!isoDateString) return "00/00/00";
    try {
        const d = new Date(isoDateString);
        if (isNaN(d.getTime())) return "00/00/00";
        const day = String(d.getDate()).padStart(2, '0');
        const month = String(d.getMonth() + 1).padStart(2, '0');
        const year = String(d.getFullYear()).slice(-2);
        return `${day}/${month}/${year}`;
    } catch {
        return "00/00/00";
    }
}

async function checkSupabaseUpdates() {
    try {
        const endpoint = `${SUPABASE_PROJECT_URL}/rest/v1/launcher_updates?select=*&order=id.desc&limit=1`;
        const res = await fetch(endpoint, {
            headers: {
                "apikey": SUPABASE_ANON_KEY,
                "Authorization": `Bearer ${SUPABASE_ANON_KEY}`
            }
        });

        if (!res.ok) return;

        const data = await res.json();
        if (!data || !Array.isArray(data) || data.length === 0) return;

        const latest = data[0];
        const remoteVersion = latest.version || "1.0.0";

        // Si la version distante est supérieure à la version actuelle du launcher
        if (compareSemVer(remoteVersion, CURRENT_LAUNCHER_VERSION) > 0) {
            showUpdateModal(latest);
        }
    } catch (e) {
        console.warn("[UpdateChecker] Erreur de vérification des mises à jour :", e);
    }
}

function showUpdateModal(updateData) {
    const modal = document.getElementById("update-modal");
    if (!modal) return;

    const verEl = document.getElementById("update-version-text");
    const dateEl = document.getElementById("update-date-text");
    const btnUpdate = document.getElementById("btn-do-update");

    const vStr = updateData.version ? (updateData.version.startsWith("v") ? updateData.version : `v${updateData.version}`) : "v0.0.0";
    if (verEl) verEl.textContent = vStr;

    if (dateEl) {
        dateEl.textContent = formatDateFr(updateData.created_at);
    }

    if (btnUpdate) {
        btnUpdate.onclick = async () => {
            const url = updateData.download_url || "https://github.com";
            const bridge = getBridge();
            if (bridge && bridge.OpenExternalUrl) {
                await bridge.OpenExternalUrl(url);
            } else {
                window.open(url, "_blank");
            }
        };
    }

    modal.style.display = "flex";

    // Désactive les boutons de lancement pour inciter à la mise à jour
    const btnPlay = document.getElementById("btn-play-yw3");
    if (btnPlay) {
        btnPlay.classList.add("disabled");
        btnPlay.title = "Veuillez mettre à jour le launcher.";
    }
}

// ==========================================
// INITIALIZATION
// ==========================================
document.addEventListener("DOMContentLoaded", () => {
    initNavigation();
    initProfile();
    initCharacterWheel();
    initMultiplayerHandlers();
    initSettingsAndControls();
    loadZakuroSettings();
    loadHostIp();
    checkSupabaseUpdates();
});

// Sidebar Navigation
function initNavigation() {
    const navBtns = document.querySelectorAll(".nav-btn");
    navBtns.forEach(btn => {
        btn.addEventListener("click", () => {
            const targetTab = btn.getAttribute("data-tab");
            switchTab(targetTab);
        });
    });

    document.getElementById("btn-goto-mp")?.addEventListener("click", () => {
        switchTab("multiplayer");
    });

    document.getElementById("btn-play-yw3")?.addEventListener("click", async () => {
        const bridge = getBridge();
        if (bridge) {
            showToast("Lancement de Yo-kai Watch 3 Ultimate PC...");
            await bridge.LaunchGame("yw3", false);
        } else {
            showToast("[Mode Démo] Commande : yokaiwatch3.exe lancée !");
        }
    });

    document.getElementById("btn-open-saves")?.addEventListener("click", async () => {
        const bridge = getBridge();
        if (bridge) await bridge.OpenFolder("saves");
    });

    document.getElementById("btn-open-studio")?.addEventListener("click", async () => {
        const bridge = getBridge();
        if (bridge) await bridge.LaunchStudio();
    });
}

function switchTab(tabId) {
    document.querySelectorAll(".nav-btn").forEach(b => {
        b.classList.toggle("active", b.getAttribute("data-tab") === tabId);
    });
    document.querySelectorAll(".tab-pane").forEach(pane => {
        pane.classList.toggle("active", pane.id === `tab-${tabId}`);
    });
}

// Profile
function initProfile() {
    const savedName = localStorage.getItem("ykw_player_name");
    const firstSetupDone = localStorage.getItem("ykw_first_setup");

    if (!savedName || !firstSetupDone) {
        openWelcomeModal();
    } else {
        state.playerName = savedName;
    }

    updateProfileUI();

    const profileModal = document.getElementById("profile-modal");
    const inputName = document.getElementById("input-profile-name");

    document.getElementById("btn-edit-profile")?.addEventListener("click", () => {
        if (inputName) inputName.value = state.playerName;
        if (profileModal) profileModal.style.display = "flex";
        setTimeout(() => inputName?.focus(), 50);
    });

    document.getElementById("btn-cancel-profile")?.addEventListener("click", () => {
        if (profileModal) profileModal.style.display = "none";
    });

    document.getElementById("btn-save-profile")?.addEventListener("click", () => {
        const newName = inputName?.value.trim();
        if (!newName || newName.length < 2) {
            showToast("Le pseudo doit contenir au moins 2 caractères !");
            return;
        }

        // Check duplicate if currently inside a multiplayer room
        if (state.inRoom && state.currentRoom?.players) {
            const isTaken = state.currentRoom.players.some(p => p.name.toLowerCase() === newName.toLowerCase() && p.name.toLowerCase() !== state.playerName.toLowerCase());
            if (isTaken) {
                showToast("Ce pseudo est déjà utilisé par un joueur dans ce salon !");
                return;
            }
        }

        state.playerName = newName;
        localStorage.setItem("ykw_player_name", state.playerName);
        localStorage.setItem("ykw_first_setup", "true");
        updateProfileUI();
        if (profileModal) profileModal.style.display = "none";
        showToast("Pseudo mis à jour !");
    });

    initWelcomeModal();
}

function updateProfileUI() {
    const nameEl = document.getElementById("sidebar-player-name");
    const avatarEl = document.getElementById("sidebar-avatar");
    if (nameEl) nameEl.textContent = state.playerName;
    if (avatarEl) avatarEl.textContent = state.playerName.charAt(0).toUpperCase();
}

function openWelcomeModal() {
    const welcomeModal = document.getElementById("welcome-modal");
    const inputWelcome = document.getElementById("input-welcome-name");
    if (welcomeModal) welcomeModal.style.display = "flex";
    if (inputWelcome) {
        inputWelcome.value = "";
        setTimeout(() => inputWelcome.focus(), 100);
    }
}

function initWelcomeModal() {
    const welcomeModal = document.getElementById("welcome-modal");
    const inputWelcome = document.getElementById("input-welcome-name");
    const btnSave = document.getElementById("btn-save-welcome");

    const submitWelcome = () => {
        const chosen = inputWelcome?.value.trim();
        if (!chosen || chosen.length < 2) {
            showToast("Veuillez saisir un pseudo d'au moins 2 caractères !");
            inputWelcome?.focus();
            return;
        }
        state.playerName = chosen;
        localStorage.setItem("ykw_player_name", chosen);
        localStorage.setItem("ykw_first_setup", "true");
        updateProfileUI();
        if (welcomeModal) welcomeModal.style.display = "none";
        showToast(`Bienvenue ${chosen} ! Profil configuré.`);
    };

    btnSave?.addEventListener("click", submitWelcome);
    inputWelcome?.addEventListener("keydown", (e) => {
        if (e.key === "Enter") submitWelcome();
    });
}

// ==========================================
// CAROUSEL / ROUE DE SÉLECTION DU PERSONNAGE
// ==========================================
function initCharacterWheel() {
    renderCharacterWheel();

    const bridge = getBridge();
    const cur = CHARACTERS.find(c => c.id === state.selectedCharacterId) || CHARACTERS[0];
    if (bridge && cur) {
        bridge.SelectCharacter(cur.id, cur.name);
    }

}

function renderCharacterWheel() {
    const track = document.getElementById("character-wheel-track");
    if (!track) return;
    track.innerHTML = "";

    const lockedChars = state.currentRoom?.lockedCharacters || [];
    const frozen = state.localInGame;

    const hint = document.getElementById("char-lock-hint");
    if (hint) {
        hint.textContent = frozen
            ? "Votre jeu est lancé : le personnage est verrouillé jusqu'à ce que vous le quittiez."
            : "Cliquez sur un personnage pour le prendre. Un personnage par joueur.";
        hint.classList.toggle("locked", !!frozen);
    }

    CHARACTERS.forEach(char => {
        const isSelected = char.id === state.selectedCharacterId;
        const isTaken = lockedChars.includes(char.id) && !isSelected;
        const owner = isTaken ? state.currentRoom?.players?.find(p => p.characterId === char.id) : null;

        const tile = document.createElement("div");
        tile.className = `char-tile ${isSelected ? "selected" : ""} ${isTaken ? "taken" : ""} ${frozen ? "frozen" : ""}`;
        tile.style.setProperty("--char-color", char.color);
        tile.title = isTaken ? `Déjà pris par ${owner ? owner.name : "un autre joueur"}` : char.name;

        let tag = "";
        if (isSelected) tag = "Votre personnage";
        else if (isTaken) tag = owner ? escapeHtml(owner.name) : "Pris";

        tile.innerHTML = `
            <span class="char-tile-check"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3"><polyline points="20 6 9 17 4 12"></polyline></svg></span>
            <div class="char-tile-avatar">${char.initial}</div>
            <div class="char-tile-name">${char.name}</div>
            <div class="char-tile-tag">${tag}</div>
        `;

        if (!isTaken && !isSelected && !frozen) {
            tile.addEventListener("click", () => selectCharacter(char.id));
        }
        track.appendChild(tile);
    });

    updateCharacterPreview();
}

async function selectCharacter(charId) {
    if (state.localInGame) {
        showToast("Quittez le jeu pour changer de personnage.");
        return;
    }
    const char = CHARACTERS.find(c => c.id === charId);
    if (!char) return;

    const previous = state.selectedCharacterId;
    state.selectedCharacterId = charId;
    renderCharacterWheel();

    const bridge = getBridge();
    if (bridge) {
        const ok = await bridge.SelectCharacter(char.id, char.name);
        if (ok === false) {
            state.selectedCharacterId = previous;
            renderCharacterWheel();
            showToast("Impossible de prendre ce personnage pour le moment.");
        }
    }
}

function updateCharacterPreview() {
    const card = document.getElementById("char-preview-card");
    if (!card) return;

    const char = CHARACTERS.find(c => c.id === state.selectedCharacterId) || CHARACTERS[0];
    card.style.setProperty("--char-color", char.color);
    card.innerHTML = `
        <div class="char-detail-avatar">${char.initial}</div>
        <div>
            <h4>${char.name}<small>${char.jp}</small></h4>
            <p>${char.title} · ${char.watch}</p>
            <p>${char.bio}</p>
        </div>
    `;
}

// ==========================================
// MULTIPLAYER LOBBY HANDLERS
// ==========================================
async function loadHostIp() {
    const bridge = getBridge();
    const entryIp = document.getElementById("entry-local-ip");
    if (bridge && entryIp) {
        try {
            const ip = await bridge.GetHostIp();
            if (ip) entryIp.textContent = ip;
        } catch { }
    }
}

function initMultiplayerHandlers() {
    // Create Room
    document.getElementById("btn-create-room")?.addEventListener("click", async () => {
        const port = parseInt(document.getElementById("host-port")?.value || "7777");
        const char = CHARACTERS.find(c => c.id === state.selectedCharacterId) || CHARACTERS[0];
        const bridge = getBridge();

        if (bridge) {
            showToast("Création du salon en cours...");
            const resJson = await bridge.CreateRoom(state.playerName, char.id, char.name, port);
            const res = JSON.parse(resJson);
            if (res.success) {
                enterRoomView(res.code, true, res.hostIp, null);
                showToast(`Salon ${res.code} créé ! (IP : ${res.hostIp})`);
            } else {
                showToast(`Erreur : ${res.error}`);
            }
        } else {
            // Demo fallback
            enterRoomView("YW3-9X", true, "10.0.0.3", null);
            showToast("Mode Démo : Salon YW3-9X créé !");
        }
    });

    // Join Room
    document.getElementById("btn-join-room")?.addEventListener("click", async () => {
        const codeInput = document.getElementById("join-code");
        const ipInput = document.getElementById("join-ip");
        const code = codeInput?.value.trim().toUpperCase() || "";
        const ip = ipInput?.value.trim() || "";

        if (code.length < 4) {
            showToast("Veuillez entrer un code de salon valide !");
            return;
        }

        const char = CHARACTERS.find(c => c.id === state.selectedCharacterId) || CHARACTERS[0];
        const bridge = getBridge();

        if (bridge) {
            showToast(`Connexion au salon ${code}...`);
            const resJson = await bridge.JoinRoom(code, state.playerName, char.id, char.name, ip, 7777);
            const res = JSON.parse(resJson);
            if (res.success) {
                enterRoomView(code, false, res.hostIp, res.room);
                showToast(`Connecté au salon ${code} !`);
            } else {
                showToast(`Erreur : ${res.error || "Impossible de rejoindre le salon"}`);
                if (res.error && res.error.toLowerCase().includes("pseudo")) {
                    setTimeout(() => {
                        document.getElementById("btn-edit-profile")?.click();
                    }, 1200);
                }
            }
        } else {
            // Demo fallback
            enterRoomView(code, false, "10.0.0.3", null);
            showToast(`Mode Démo : Connecté au salon ${code} !`);
        }
    });

    // Copy Code
    document.getElementById("btn-copy-code")?.addEventListener("click", () => {
        const code = document.getElementById("room-code-display")?.textContent || "";
        navigator.clipboard.writeText(code).then(() => {
            showToast(`Code ${code} copié dans le presse-papiers !`);
        });
    });

    // Copy IP
    document.getElementById("btn-copy-ip")?.addEventListener("click", () => {
        const full = document.getElementById("room-ip-display")?.textContent || "";
        const cleanIp = full.split(":")[0];
        navigator.clipboard.writeText(cleanIp).then(() => {
            showToast(`Adresse IP hôte (${cleanIp}) copiée !`);
        });
    });

    // Leave Room
    document.getElementById("btn-leave-room")?.addEventListener("click", () => {
        const bridge = getBridge();
        if (bridge) bridge.LeaveRoom();
        exitRoomView();
        showToast("Vous avez quitté le salon.");
    });

    // Lancer son propre jeu : chacun lance quand il veut
    document.getElementById("btn-start-multiplayer")?.addEventListener("click", async () => {
        if (state.localInGame) return;
        const bridge = getBridge();
        if (bridge) {
            const ok = await bridge.LaunchGame("yw3", true);
            if (ok) {
                state.localInGame = true;
                renderCharacterWheel();
                renderPlayerSlots();
                showToast("Lancement du jeu...");
            }
        } else {
            showToast("[Mode Démo] Jeu lancé !");
        }
    });
}

function enterRoomView(code, isHost, hostIp, room) {
    state.inRoom = true;
    state.isHost = isHost;
    if (room) {
        state.currentRoom = room;
    }

    document.getElementById("mp-lobby-entry").style.display = "none";
    document.getElementById("mp-lobby-room").style.display = "block";

    const codeDisplay = document.getElementById("room-code-display");
    if (codeDisplay) codeDisplay.textContent = code;

    const ipDisplay = document.getElementById("room-ip-display");
    const ipVal = hostIp || state.currentRoom?.hostIp || "127.0.0.1";
    const portVal = state.currentRoom?.port || 7777;
    if (ipDisplay) ipDisplay.textContent = `${ipVal}:${portVal}`;


    renderPlayerSlots();
    renderCharacterWheel();
}

function exitRoomView() {
    state.inRoom = false;
    state.localPlayerId = null;
    state.isHost = false;
    state.currentRoom = null;

    document.getElementById("mp-lobby-entry").style.display = "block";
    document.getElementById("mp-lobby-room").style.display = "none";
}

function renderPlayerSlots() {
    const container = document.getElementById("room-slots-container");
    if (!container) return;
    container.innerHTML = "";

    const myId = state.localPlayerId;
    const players = state.currentRoom?.players && state.currentRoom.players.length > 0
        ? state.currentRoom.players
        : [{
            id: myId,
            name: state.playerName,
            characterId: state.selectedCharacterId,
            isHost: state.isHost,
            inGame: state.localInGame
        }];

    const playerCount = document.getElementById("room-player-count");
    if (playerCount) playerCount.textContent = `${players.length} / 4`;

    for (let i = 0; i < 4; i++) {
        const player = players[i];
        const row = document.createElement("div");
        if (player) {
            const char = CHARACTERS.find(c => c.id === player.characterId) || CHARACTERS[0];
            const isMe = myId ? player.id === myId : player.name === state.playerName;
            row.className = `member-row ${isMe ? "me" : ""}`;
            row.style.setProperty("--slot-char-color", char.color);
            row.innerHTML = `
                <div class="member-avatar">${char.initial}</div>
                <div class="member-info">
                    <div class="member-name">${escapeHtml(player.name)}${player.isHost ? '<span class="member-host">HÔTE</span>' : ""}${isMe ? '<span class="member-you">(vous)</span>' : ""}</div>
                    <div class="member-char">${char.name}</div>
                </div>
                <span class="member-status ${player.inGame ? "ingame" : ""}">${player.inGame ? "En jeu" : "Dans le salon"}</span>
            `;
        } else {
            row.className = "member-row empty";
            row.textContent = "Place libre";
        }
        container.appendChild(row);
    }

    updateLaunchButton();
}

function updateLaunchButton() {
    const btn = document.getElementById("btn-start-multiplayer");
    const label = document.getElementById("launch-label");
    const hint = document.getElementById("launch-hint");
    if (!btn || !label) return;
    btn.disabled = !!state.localInGame;
    label.textContent = state.localInGame ? "JEU EN COURS" : "LANCER MON JEU";
    if (hint) {
        hint.textContent = state.localInGame
            ? "Fermez le jeu pour pouvoir changer de personnage."
            : "Chacun lance son jeu quand il veut. Vous vous verrez dès que vous serez sur la même zone.";
    }
}

const HTML_ESCAPES = { "&": "&amp;", "<": "&lt;", ">": "&gt;", "\"": "&quot;", "'": "&#39;" };
function escapeHtml(text) {
    return String(text ?? "").replace(/[&<>"']/g, c => HTML_ESCAPES[c]);
}

// C# WebSocket / Named event hooks called by WebView2
window.onRoomUpdated = function (data) {
    if (!data || !data.room) return;
    state.currentRoom = data.room;
    state.isHost = data.isHost;

    const ipDisplay = document.getElementById("room-ip-display");
    if (ipDisplay && data.room.hostIp) {
        ipDisplay.textContent = `${data.room.hostIp}:${data.room.port || 7777}`;
    }

    if (data.localPlayer?.characterId && data.localPlayer.characterId !== state.selectedCharacterId) {
        state.selectedCharacterId = data.localPlayer.characterId;
    }
    if (data.localPlayer) {
        state.localPlayerId = data.localPlayer.id || state.localPlayerId;
        state.localInGame = !!data.localPlayer.inGame;
    }

    renderCharacterWheel();
    renderPlayerSlots();
};

window.onRoomLeft = function () {
    exitRoomView();
    showToast("Vous avez été déconnecté du salon.");
};

window.onLobbyLog = function (msg) {
    const logsBody = document.getElementById("lobby-logs-container");
    if (!logsBody) return;
    const line = document.createElement("div");
    line.className = "log-line";
    line.textContent = msg;
    logsBody.appendChild(line);
    logsBody.scrollTop = logsBody.scrollHeight;
};

// le jeu de ce joueur vient de démarrer ou de se fermer
window.onLocalGameState = function (running) {
    state.localInGame = !!running;
    renderCharacterWheel();
    renderPlayerSlots();
};

window.onGameStarted = function (code) {
    showToast(`Session multijoueur [${code}] en cours ! Lancement du jeu...`);
};

// ==========================================
// SETTINGS & CONTROLS BINDING
// ==========================================
const KEY_LABELS = {
    a: "Bouton A (Action / Valider)",
    b: "Bouton B (Annuler / Courir)",
    x: "Bouton X (Menu / Inventaire)",
    y: "Bouton Y (Radar Yo-kai)",
    l: "Gâchette L",
    r: "Gâchette R",
    start: "Start (Pause)",
    select: "Select (Carte)",
    up: "D-Pad Haut",
    down: "D-Pad Bas",
    left: "D-Pad Gauche",
    right: "D-Pad Droite",
    circle_up: "Stick Analogique Haut",
    circle_down: "Stick Analogique Bas",
    circle_left: "Stick Analogique Gauche",
    circle_right: "Stick Analogique Droite"
};

const PAD_LABELS = {
    a: "Bouton A",
    b: "Bouton B",
    x: "Bouton X",
    y: "Bouton Y",
    l: "Gâchette L (LB)",
    r: "Gâchette R (RB)",
    start: "Bouton Start",
    select: "Bouton Select / Back",
    up: "D-Pad Haut",
    down: "D-Pad Bas",
    left: "D-Pad Gauche",
    right: "D-Pad Droite"
};

async function loadZakuroSettings() {
    const bridge = getBridge();
    let settings = null;

    if (bridge) {
        try {
            const json = await bridge.GetSettings();
            settings = JSON.parse(json);
        } catch { }
    }

    if (!settings) {
        // Fallback default
        settings = {
            renderer: "vulkan",
            resolution: 4,
            fullscreen: true,
            stretch: true,
            show_fps: true,
            volume: 1.0,
            keys: {
                a: "KeyF", b: "ShiftLeft", x: "KeyX", y: "KeyY",
                l: "KeyQ", r: "KeyE", start: "Enter", select: "Backspace",
                up: "KeyW", down: "KeyS", left: "KeyA", right: "KeyD",
                circle_up: "KeyW", circle_down: "KeyS", circle_left: "KeyA", circle_right: "KeyD"
            },
            pad: {
                a: "East", b: "South", x: "North", y: "West",
                l: "LeftTrigger", r: "RightTrigger", start: "Start", select: "Select",
                up: "DPadUp", down: "DPadDown", left: "DPadLeft", right: "DPadRight"
            }
        };
    }

    state.settings = settings;
    populateSettingsUI();
    populateControlsUI();
}

function populateSettingsUI() {
    if (!state.settings) return;

    const s = state.settings;
    const selRenderer = document.getElementById("setting-renderer");
    const sldRes = document.getElementById("setting-resolution");
    const valRes = document.getElementById("val-resolution");
    const chkFull = document.getElementById("setting-fullscreen");
    const chkStretch = document.getElementById("setting-stretch");
    const chkFps = document.getElementById("setting-fps");
    const sldVol = document.getElementById("setting-volume");
    const valVol = document.getElementById("val-volume");

    if (selRenderer) selRenderer.value = s.renderer || "vulkan";
    if (sldRes) {
        sldRes.value = s.resolution || 4;
        const resText = { 1: "1x (240p)", 2: "2x (480p)", 3: "3x (720p)", 4: "4x (1440p)", 5: "5x (1800p)", 6: "6x (4K)" };
        if (valRes) valRes.textContent = resText[sldRes.value] || `${sldRes.value}x`;
    }
    if (chkFull) chkFull.checked = !!s.fullscreen;
    if (chkStretch) chkStretch.checked = !!s.stretch;
    if (chkFps) chkFps.checked = !!s.show_fps;
    const selFps = document.getElementById("setting-fps-target");
    const fpsVal = s.target_fps || 60;
    if (selFps) selFps.value = fpsVal.toString();
    const badgeFps = document.getElementById("badge-game-fps");
    if (badgeFps) badgeFps.textContent = `${fpsVal} FPS`;
    if (sldVol) {
        const volInt = Math.round((s.volume ?? 1.0) * 100);
        sldVol.value = volInt;
        if (valVol) valVol.textContent = `${volInt}%`;
    }
}

function populateControlsUI() {
    const kGrid = document.getElementById("keyboard-bindings-grid");
    const pGrid = document.getElementById("pad-bindings-grid");
    if (!kGrid || !pGrid || !state.settings) return;

    kGrid.innerHTML = "";
    pGrid.innerHTML = "";

    // Keyboard
    const keys = state.settings.keys || {};
    Object.keys(KEY_LABELS).forEach(k => {
        const row = document.createElement("div");
        row.className = "keybind-row";
        const val = keys[k] || "Non assigné";
        row.innerHTML = `
            <span class="keybind-label">${KEY_LABELS[k]}</span>
            <button class="key-badge" data-key="${k}">${formatKeyName(val)}</button>
        `;
        row.querySelector(".key-badge")?.addEventListener("click", () => {
            openRebindModal("keys", k, KEY_LABELS[k]);
        });
        kGrid.appendChild(row);
    });

    // Pad
    const pad = state.settings.pad || {};
    Object.keys(PAD_LABELS).forEach(p => {
        const row = document.createElement("div");
        row.className = "keybind-row";
        const val = pad[p] || "Non assigné";
        row.innerHTML = `
            <span class="keybind-label">${PAD_LABELS[p]}</span>
            <span class="badge-pill" style="color:#fff;">${val}</span>
        `;
        pGrid.appendChild(row);
    });
}

function formatKeyName(winitCode) {
    if (!winitCode) return "...";
    if (winitCode.startsWith("Key")) return winitCode.substring(3);
    if (winitCode.startsWith("Digit")) return winitCode.substring(5);
    return winitCode;
}

function initSettingsAndControls() {
    // Sliders live text
    const sldRes = document.getElementById("setting-resolution");
    const valRes = document.getElementById("val-resolution");
    sldRes?.addEventListener("input", () => {
        const resText = { 1: "1x (240p)", 2: "2x (480p)", 3: "3x (720p)", 4: "4x (1440p)", 5: "5x (1800p)", 6: "6x (4K)" };
        if (valRes) valRes.textContent = resText[sldRes.value] || `${sldRes.value}x`;
    });

    const sldVol = document.getElementById("setting-volume");
    const valVol = document.getElementById("val-volume");
    sldVol?.addEventListener("input", () => {
        if (valVol) valVol.textContent = `${sldVol.value}%`;
    });

    // Save Settings
    document.getElementById("btn-save-settings")?.addEventListener("click", async () => {
        if (!state.settings) return;
        state.settings.renderer = document.getElementById("setting-renderer")?.value || "vulkan";
        state.settings.resolution = parseInt(document.getElementById("setting-resolution")?.value || "4");
        state.settings.fullscreen = !!document.getElementById("setting-fullscreen")?.checked;
        state.settings.stretch = !!document.getElementById("setting-stretch")?.checked;
        state.settings.show_fps = !!document.getElementById("setting-fps")?.checked;
        const targetFps = parseInt(document.getElementById("setting-fps-target")?.value || "60");
        state.settings.target_fps = targetFps;
        const badgeFps = document.getElementById("badge-game-fps");
        if (badgeFps) badgeFps.textContent = `${targetFps} FPS`;
        state.settings.volume = parseInt(document.getElementById("setting-volume")?.value || "100") / 100.0;

        const bridge = getBridge();
        if (bridge) {
            await bridge.SaveSettings(JSON.stringify(state.settings));
            showToast(`Paramètres sauvegardés (Fréquence : ${targetFps} FPS) !`);
        } else {
            showToast(`[Mode Démo] Paramètres enregistrés (${targetFps} FPS) !`);
        }
    });

    // Default keys button
    document.getElementById("btn-default-keys")?.addEventListener("click", async () => {
        if (!state.settings) return;
        state.settings.keys = {
            a: "KeyF", b: "ShiftLeft", x: "KeyX", y: "KeyY",
            l: "KeyQ", r: "KeyE", start: "Enter", select: "Backspace",
            up: "KeyW", down: "KeyS", left: "KeyA", right: "KeyD",
            circle_up: "KeyW", circle_down: "KeyS", circle_left: "KeyA", circle_right: "KeyD"
        };
        populateControlsUI();
        const bridge = getBridge();
        if (bridge) await bridge.SaveSettings(JSON.stringify(state.settings));
        showToast("Touches par défaut restaurées (ZQSD) !");
    });

    // Rebind Modal Handlers
    const rebindModal = document.getElementById("rebind-modal");
    document.getElementById("btn-cancel-rebind")?.addEventListener("click", () => {
        state.rebindAction = null;
        if (rebindModal) rebindModal.style.display = "none";
    });

    window.addEventListener("keydown", async (e) => {
        if (!state.rebindAction || !rebindModal || rebindModal.style.display !== "flex") return;
        e.preventDefault();

        // Convert standard e.code to winit KeyCode
        let code = e.code;
        if (state.settings && state.settings.keys) {
            state.settings.keys[state.rebindAction] = code;
            populateControlsUI();

            const bridge = getBridge();
            if (bridge) await bridge.SaveSettings(JSON.stringify(state.settings));

            showToast(`Touche ${KEY_LABELS[state.rebindAction]} -> ${formatKeyName(code)}`);
        }

        state.rebindAction = null;
        rebindModal.style.display = "none";
    });
}

function openRebindModal(type, actionKey, label) {
    state.rebindType = type;
    state.rebindAction = actionKey;
    const nameEl = document.getElementById("rebind-action-name");
    const modal = document.getElementById("rebind-modal");
    if (nameEl) nameEl.textContent = label;
    if (modal) modal.style.display = "flex";
}
