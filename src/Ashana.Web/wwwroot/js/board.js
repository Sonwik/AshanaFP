// Live updates for the pickup TV (/board). Plain JavaScript + the SignalR client, no framework.
// The server renders the first snapshot; this script keeps it current and reconnects forever.
(() => {
  "use strict";

  const root = document.getElementById("board");
  if (!root || !window.signalR) return;

  const lists = {
    preparing: root.querySelector('[data-list="preparing"]'),
    ready: root.querySelector('[data-list="ready"]'),
  };
  const status = root.querySelector("[data-status]");
  const clock = root.querySelector("[data-clock]");
  const knownReady = new Set([...lists.ready.children].map((li) => li.dataset.number));

  function setStatus(state, text) {
    status.dataset.status = state;
    status.textContent = text;
  }

  // textContent only, never innerHTML: nothing from the server is ever parsed as HTML.
  function render(list, entries, highlightNew) {
    const items = entries.map((entry) => {
      const li = document.createElement("li");
      li.className = "tv-num";
      li.dataset.number = String(entry.number);
      li.textContent = String(entry.number);
      if (highlightNew && !knownReady.has(li.dataset.number)) li.classList.add("is-new");
      return li;
    });
    list.replaceChildren(...items);
  }

  function update(snapshot) {
    if (!snapshot) return;
    render(lists.preparing, snapshot.preparing ?? [], false);
    render(lists.ready, snapshot.ready ?? [], true);
    knownReady.clear();
    (snapshot.ready ?? []).forEach((entry) => knownReady.add(String(entry.number)));
  }

  const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/orders")
    .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
    .build();

  connection.on("BoardUpdated", update);
  connection.onreconnecting(() => setStatus("connecting", "Reconnecting…"));
  connection.onreconnected(async () => {
    setStatus("live", "Live");
    update(await connection.invoke("JoinBoard"));
  });
  connection.onclose(() => {
    setStatus("offline", "Offline, retrying");
    setTimeout(start, 5000);
  });

  async function start() {
    try {
      await connection.start();
      setStatus("live", "Live");
      update(await connection.invoke("JoinBoard"));
    } catch {
      setStatus("offline", "Offline, retrying");
      setTimeout(start, 5000);
    }
  }

  function tick() {
    const now = new Date();
    clock.textContent = `${String(now.getHours()).padStart(2, "0")}:${String(now.getMinutes()).padStart(2, "0")}`;
  }

  setInterval(tick, 15000);
  start();
})();
