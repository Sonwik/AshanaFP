// Small app-wide browser glue, loaded once as an ES module after blazor.web.js.

const THEME_KEY = "ashana.theme";

function applySavedTheme() {
  try {
    const theme = localStorage.getItem(THEME_KEY);
    if (theme === "light" || theme === "dark") document.documentElement.dataset.theme = theme;
  } catch {
    // storage blocked: keep the system theme
  }
}

// Enhanced navigation between static pages (sign-in → menu) patches the document; keep the chosen theme.
window.Blazor?.addEventListener?.("enhancedload", applySavedTheme);

// Service worker: makes the app installable and shows a friendly page when the network is gone.
const secure = location.protocol === "https:" || location.hostname === "localhost" || location.hostname === "127.0.0.1";
if ("serviceWorker" in navigator && secure) {
  window.addEventListener("load", () => {
    navigator.serviceWorker.register("/sw.js").catch(() => { /* not critical */ });
  });
}
