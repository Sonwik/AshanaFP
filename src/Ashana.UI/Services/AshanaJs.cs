using Microsoft.JSInterop;

namespace Ashana.UI;

/// <summary>
/// Typed wrapper around <c>ashana-ui.js</c>. The ES module is imported lazily the first time it is needed
/// (lazy loading), and every call tolerates a closed browser tab: in Blazor Server the circuit can disappear
/// at any moment, which throws <see cref="JSDisconnectedException"/>.
/// </summary>
public sealed class AshanaJs : IAsyncDisposable
{
    private readonly Lazy<Task<IJSObjectReference>> _module;

    public AshanaJs(IJSRuntime js)
    {
        _module = new(() => js.InvokeAsync<IJSObjectReference>("import", "./_content/Ashana.UI/ashana-ui.js").AsTask());
    }

    public Task<string> GetThemeAsync() => CallAsync("getTheme", "light");

    public Task SetThemeAsync(string theme) => CallVoidAsync("setTheme", theme);

    public Task ShowDialogAsync(object dialog, object dotNetRef) => CallVoidAsync("showDialog", dialog, dotNetRef);

    public Task CloseDialogAsync(object dialog) => CallVoidAsync("closeDialog", dialog);

    public Task FocusAsync(object element) => CallVoidAsync("focusElement", element);

    public Task ScrollToIdAsync(string id) => CallVoidAsync("scrollToId", id);

    public Task DownloadTextAsync(string fileName, string text, string mimeType = "text/plain;charset=utf-8") =>
        CallVoidAsync("downloadText", fileName, text, mimeType);

    public Task<string> NotificationPermissionAsync() => CallAsync("notificationPermission", "unsupported");

    public Task<string> RequestNotificationsAsync() => CallAsync("requestNotifications", "unsupported");

    public Task<bool> NotifyAsync(string title, string body) => CallAsync("notify", false, title, body);

    public Task<bool> ChimeAsync() => CallAsync("chime", false);

    public Task<bool> VibrateAsync(int milliseconds) => CallAsync("vibrate", false, milliseconds);

    public async ValueTask DisposeAsync()
    {
        if (!_module.IsValueCreated)
        {
            return;
        }

        try
        {
            var module = await _module.Value;
            await module.DisposeAsync();
        }
        catch (Exception ex) when (IsBrowserGone(ex))
        {
        }
    }

    private async Task<T> CallAsync<T>(string identifier, T fallback, params object?[] args)
    {
        try
        {
            var module = await _module.Value;
            return await module.InvokeAsync<T>(identifier, args);
        }
        catch (Exception ex) when (IsBrowserGone(ex))
        {
            return fallback;
        }
    }

    private async Task CallVoidAsync(string identifier, params object?[] args)
    {
        try
        {
            var module = await _module.Value;
            await module.InvokeVoidAsync(identifier, args);
        }
        catch (Exception ex) when (IsBrowserGone(ex))
        {
        }
    }

    /// <summary>
    /// Disconnected tab, cancelled call, a JavaScript error, or a call during prerendering
    /// (InvalidOperationException: no browser attached yet). None of them should crash the page.
    /// </summary>
    private static bool IsBrowserGone(Exception ex) =>
        ex is JSDisconnectedException or TaskCanceledException or InvalidOperationException or JSException;
}
