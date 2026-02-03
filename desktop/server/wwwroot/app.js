const aliasInput = document.getElementById("alias");
const joinBtn = document.getElementById("joinBtn");
const setupPage = document.getElementById("setup");
const tallyPage = document.getElementById("tally");
const statusEl = document.getElementById("status");
const backBtn = document.getElementById("backBtn");

let ws;

joinBtn.addEventListener("click", () => {
  const alias = aliasInput.value.trim();
  if (!alias) return alert("Please enter a camera alias!");
  localStorage.setItem("camAlias", alias);
  startTally(alias);
});

backBtn.addEventListener("click", () => location.reload());

function startTally(alias) {
  setupPage.classList.add("hidden");
  tallyPage.classList.remove("hidden");

  ws = new WebSocket(`ws://${location.host}/ws/tally`);

  ws.onopen = () => {
    console.log("Connected to tally server");
    ws.send(JSON.stringify({ type: "join", alias })); // announce this client
  };

  ws.onmessage = (e) => {
    try {
      // server sends { Alias, PGM, PVW } for any camera it updates
      const t = JSON.parse(e.data);
      if (t && t.Alias === alias) updateStatus(t.PGM, t.PVW);
    } catch (err) {
      console.error("Bad message", err);
    }
  };

  ws.onclose = () => {
    console.warn("Disconnected. Reconnecting in 3s...");
    setTimeout(() => startTally(alias), 3000);
  };
}

function updateStatus(pgm, pvw) {
  statusEl.className = "status";
  if (pgm) {
    statusEl.classList.add("pgm");
    statusEl.textContent = "ON AIR";
    navigator.vibrate?.(80);
  } else if (pvw) {
    statusEl.classList.add("pvw");
    statusEl.textContent = "PREVIEW";
  } else {
    statusEl.classList.add("idle");
    statusEl.textContent = "IDLE";
  }
}

// preload saved alias
const saved = localStorage.getItem("camAlias");
if (saved) aliasInput.value = saved;
