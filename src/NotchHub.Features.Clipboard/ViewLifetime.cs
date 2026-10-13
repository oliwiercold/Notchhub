using System.Windows;
using NotchHub.Core;

namespace NotchHub.Features.Clipboard;

/// <summary>
/// Shown/hidden bookkeeping for a feature view (one copy per feature assembly, they only reference Core + UI):
/// * OnShown/OnHidden are idempotent;
/// * if the view leaves the visual tree while shown (the shell drops or re-parents it) it is hidden, and shown again when it
///   comes back, so timers and service subscriptions never outlive the visible view;
/// * Dispose hides it for good and runs the final release (the shell disposes long-hidden views to save RAM and creates a
///   fresh view when the tab is opened again).
/// </summary>
internal sealed class ViewLifetime : IDisposable
{
    private readonly FrameworkElement _view;
    private readonly Action _show, _hide;
    private readonly Action? _release;
    private bool _shown, _resumeOnLoad, _disposed;

    public ViewLifetime(FrameworkElement view, Action show, Action hide, Action? release = null)
    {
        _view = view;
        _show = show;
        _hide = hide;
        _release = release;
        view.Loaded += OnLoaded;
        view.Unloaded += OnUnloaded;
    }

    public bool IsShown => _shown;

    public void Show()
    {
        _resumeOnLoad = false;
        if (_disposed || _shown) return;
        _shown = true;
        _show();
    }

    public void Hide()
    {
        _resumeOnLoad = false;
        if (!_shown) return;
        _shown = false;
        _hide();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (!_shown) return;
        Log.Safe($"{_view.GetType().Name} unloaded while shown", Hide);
        _resumeOnLoad = true;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_resumeOnLoad && !_disposed) Log.Safe($"{_view.GetType().Name} reloaded", Show);
    }

    public void Dispose()
    {
        if (_disposed) return;
        Log.Safe($"{_view.GetType().Name} hide on dispose", Hide);
        _disposed = true;
        _view.Loaded -= OnLoaded;
        _view.Unloaded -= OnUnloaded;
        if (_release is not null) Log.Safe($"{_view.GetType().Name} release", _release);
    }
}
