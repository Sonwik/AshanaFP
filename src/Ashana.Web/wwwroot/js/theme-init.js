// Applies the theme saved by ThemeToggle before the first paint (loaded in <head>, so there is no flash).
// It is an external file because the Content-Security-Policy does not allow inline scripts.
(function () {
  try {
    var theme = localStorage.getItem("ashana.theme");
    if (theme === "light" || theme === "dark") {
      document.documentElement.dataset.theme = theme;
    }
  } catch (e) {
    // storage blocked (private mode): follow the system theme
  }
})();
