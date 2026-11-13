// Small browser helpers used by Ashana.UI through JS interop (see Services/AshanaJs.cs).
// Everything degrades gracefully: a missing browser feature never breaks the page.

const THEME_KEY = "ashana.theme";

export function getTheme() {
  const chosen = document.documentElement.dataset.theme;
  if (chosen === "light" || chosen === "dark") return chosen;
  return window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
}

export function setTheme(theme) {
  document.documentElement.dataset.theme = theme;
  try { localStorage.setItem(THEME_KEY, theme); } catch { /* private mode: the theme just isn't remembered */ }
  const meta = document.querySelector('meta[name="theme-color"]');
  if (meta) meta.setAttribute("content", theme === "dark" ? "#0d1430" : "#0b7380");
}

// <dialog> gives us a focus trap, Esc to close and a backdrop for free.
export function showDialog(dialog, dotnet) {
  if (!dialog || dialog.open) return;
  dialog.showModal();
  const onClose = () => {
    dialog.removeEventListener("close", onClose);
    dialog.removeEventListener("click", onBackdrop);
    dotnet.invokeMethodAsync("NotifyClosed").catch(() => { /* circuit gone */ });
  };
  const onBackdrop = (event) => { if (event.target === dialog) dialog.close(); };
  dialog.addEventListener("close", onClose);
  dialog.addEventListener("click", onBackdrop);
}

export function closeDialog(dialog) {
  if (dialog && dialog.open) dialog.close();
}

export function focusElement(element) {
  element?.focus?.({ preventScroll: false });
}

export function scrollToId(id) {
  document.getElementById(id)?.scrollIntoView({ behavior: "smooth", block: "start" });
}

export function downloadText(fileName, text, mimeType) {
  const blob = new Blob([text], { type: mimeType || "text/plain;charset=utf-8" });
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 2000);
}

export function notificationPermission() {
  return "Notification" in window ? Notification.permission : "unsupported";
}

export async function requestNotifications() {
  if (!("Notification" in window)) return "unsupported";
  try { return await Notification.requestPermission(); } catch { return "denied"; }
}

export function notify(title, body) {
  if (!("Notification" in window) || Notification.permission !== "granted") return false;
  try {
    const n = new Notification(title, { body, icon: "/icons/icon-192.png", tag: "ashana-order" });
    n.onclick = () => { window.focus(); n.close(); };
    return true;
  } catch {
    return false; // some mobile browsers only allow notifications from a service worker
  }
}

let audioContext;
// Two short tones, like a counter bell. Browsers only allow sound after the user has clicked something.
export function chime() {
  try {
    audioContext ??= new (window.AudioContext || window.webkitAudioContext)();
    const now = audioContext.currentTime;
    [880, 1320].forEach((frequency, i) => {
      const osc = audioContext.createOscillator();
      const gain = audioContext.createGain();
      osc.type = "sine";
      osc.frequency.value = frequency;
      gain.gain.setValueAtTime(0.0001, now + i * 0.18);
      gain.gain.exponentialRampToValueAtTime(0.25, now + i * 0.18 + 0.02);
      gain.gain.exponentialRampToValueAtTime(0.0001, now + i * 0.18 + 0.35);
      osc.connect(gain).connect(audioContext.destination);
      osc.start(now + i * 0.18);
      osc.stop(now + i * 0.18 + 0.4);
    });
    return true;
  } catch {
    return false;
  }
}

export function vibrate(pattern) {
  try { return navigator.vibrate?.(pattern) ?? false; } catch { return false; }
}
