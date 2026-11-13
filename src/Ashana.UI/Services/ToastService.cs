namespace Ashana.UI;

public enum ToastKind
{
    Info,
    Success,
    Warning,
    Error,
}

public sealed record Toast(Guid Id, string Message, ToastKind Kind, TimeSpan Duration);

/// <summary>
/// State container for pop-up messages (one per browser tab, registered as scoped).
/// Any component can call <see cref="Success"/> or <see cref="Error"/>; <c>ToastHost</c> renders them.
/// </summary>
public sealed class ToastService
{
    private const int MaxVisible = 4;
    private readonly List<Toast> _toasts = [];
    private readonly Lock _gate = new();

    /// <summary>Raised when the list changes. The host re-renders on it.</summary>
    public event Action? Changed;

    public IReadOnlyList<Toast> Toasts
    {
        get
        {
            lock (_gate)
            {
                return _toasts.ToList();
            }
        }
    }

    public Toast Show(string message, ToastKind kind = ToastKind.Info, TimeSpan? duration = null)
    {
        var toast = new Toast(Guid.NewGuid(), message, kind, duration ?? (kind == ToastKind.Error ? TimeSpan.FromSeconds(7) : TimeSpan.FromSeconds(4)));
        lock (_gate)
        {
            _toasts.Add(toast);
            if (_toasts.Count > MaxVisible)
            {
                _toasts.RemoveAt(0);
            }
        }

        Changed?.Invoke();
        return toast;
    }

    public Toast Success(string message) => Show(message, ToastKind.Success);

    public Toast Info(string message) => Show(message, ToastKind.Info);

    public Toast Warning(string message) => Show(message, ToastKind.Warning);

    public Toast Error(string message) => Show(message, ToastKind.Error);

    public void Dismiss(Guid id)
    {
        bool removed;
        lock (_gate)
        {
            removed = _toasts.RemoveAll(t => t.Id == id) > 0;
        }

        if (removed)
        {
            Changed?.Invoke();
        }
    }
}
