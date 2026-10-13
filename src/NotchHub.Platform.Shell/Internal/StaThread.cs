using System.Windows.Interop;
using System.Windows.Threading;
using NotchHub.Core;
using NotchHub.Platform.Shell.Internal.Interop;

namespace NotchHub.Platform.Shell.Internal;

/// <summary>Handles a window message on the shell STA thread. Return true to mark it handled.</summary>
internal delegate bool WindowMessageHandler(nint hwnd, int msg, nint wParam, nint lParam);

/// <summary>
/// One dedicated STA thread running a WPF <see cref="Dispatcher"/>, shared by the clipboard listener, hotkeys, the
/// foreground WinEvent hook and the WM_SETTINGCHANGE listener. It owns two windows:
/// <list type="bullet">
/// <item>a message-only window (parent HWND_MESSAGE) — clipboard format listener and RegisterHotKey target;</item>
/// <item>a hidden, never shown top-level tool window — message-only windows do not receive broadcasts such as
/// WM_SETTINGCHANGE / WM_DISPLAYCHANGE, so those arrive here.</item>
/// </list>
/// The thread is reference counted through <see cref="Acquire"/>: it starts with the first lease and shuts down
/// cleanly when the last lease is disposed.
/// </summary>
internal sealed class StaThread
{
    private static readonly object Gate = new();
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(5);
    private static StaThread? _current;

    private readonly ManualResetEventSlim _ready = new();
    private readonly object _handlersGate = new();
    private WindowMessageHandler[] _handlers = [];
    private Thread? _thread;
    private Dispatcher? _dispatcher;
    private HwndSource? _messageWindow;
    private HwndSource? _broadcastWindow;
    private Exception? _startError;
    private int _refCount;
    private bool _stopping;

    private StaThread() { }

    /// <summary>Message-only window (clipboard listener / hotkey target).</summary>
    public nint MessageWindow { get; private set; }

    /// <summary>Hidden top-level window that receives broadcast messages.</summary>
    public nint BroadcastWindow { get; private set; }

    public Dispatcher Dispatcher => _dispatcher ?? throw new InvalidOperationException("STA thread is not running.");

    public bool IsAlive => _dispatcher is { HasShutdownStarted: false };

    /// <summary>
    /// Gets a lease on the shared thread, starting it if needed. Blocks until the windows exist (normally a few ms).
    /// Throws when the thread cannot be started; callers degrade gracefully.
    /// </summary>
    public static Lease Acquire()
    {
        StaThread sta;
        lock (Gate)
        {
            if (_current is null || _current._stopping)
            {
                _current = new StaThread();
                _current.Start();
            }
            sta = _current;
            sta._refCount++;
        }

        var lease = new Lease(sta);
        if (!sta._ready.Wait(StartTimeout) || sta._startError is not null)
        {
            lease.Dispose();
            throw new InvalidOperationException("The shell STA thread failed to start.", sta._startError);
        }
        return lease;
    }

    /// <summary><see cref="Acquire"/> off the calling thread (StartAsync paths must not block the UI thread).</summary>
    public static Task<Lease> AcquireAsync() => Task.Run(Acquire);

    public bool CheckAccess() => _dispatcher?.CheckAccess() == true;

    /// <summary>Registers a handler for messages of both windows. Dispose the result to remove it.</summary>
    public IDisposable AddMessageHandler(WindowMessageHandler handler)
    {
        lock (_handlersGate) _handlers = [.. _handlers, handler];
        return new ActionDisposable(() =>
        {
            lock (_handlersGate) _handlers = _handlers.Where(h => h != handler).ToArray();
        });
    }

    /// <summary>Runs <paramref name="action"/> on the STA thread.</summary>
    public Task InvokeAsync(Action action) => Dispatcher.InvokeAsync(action).Task;

    /// <summary>Runs <paramref name="func"/> on the STA thread.</summary>
    public Task<T> InvokeAsync<T>(Func<T> func) => Dispatcher.InvokeAsync(func).Task;

    /// <summary>Runs an async function on the STA thread; its awaits resume on the STA thread too.</summary>
    public Task<T> InvokeAsync<T>(Func<Task<T>> func) => Dispatcher.InvokeAsync(func).Task.Unwrap();

    /// <summary>
    /// Runs <paramref name="func"/> synchronously on the STA thread (inline when already on it). Returns
    /// <paramref name="fallback"/> when the thread is gone or the call does not start within <paramref name="timeout"/>.
    /// </summary>
    public T Invoke<T>(Func<T> func, T fallback, TimeSpan timeout)
    {
        var dispatcher = _dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted) return fallback;
        if (dispatcher.CheckAccess()) return func();
        try
        {
            var completed = false;
            var result = fallback;
            dispatcher.Invoke(() => { result = func(); completed = true; }, DispatcherPriority.Send, CancellationToken.None, timeout);
            return completed ? result : fallback;
        }
        catch (OperationCanceledException)
        {
            return fallback;
        }
    }

    private void Start()
    {
        _thread = new Thread(Run)
        {
            Name = "NotchHub.Shell.STA",
            IsBackground = true,
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    private void Run()
    {
        try
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            _dispatcher.UnhandledException += OnUnhandledException;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(_dispatcher));

            _messageWindow = new HwndSource(new HwndSourceParameters("NotchHub.Shell.Messages")
            {
                ParentWindow = Win32.HWND_MESSAGE,
                WindowStyle = 0,
                Width = 0,
                Height = 0,
            });
            _messageWindow.AddHook(WndProc);
            MessageWindow = _messageWindow.Handle;

            _broadcastWindow = new HwndSource(new HwndSourceParameters("NotchHub.Shell.Broadcasts")
            {
                WindowStyle = unchecked((int)Win32.WS_POPUP),
                ExtendedWindowStyle = Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE,
                PositionX = -32000,
                PositionY = -32000,
                Width = 0,
                Height = 0,
            });
            _broadcastWindow.AddHook(WndProc);
            BroadcastWindow = _broadcastWindow.Handle;
        }
        catch (Exception ex)
        {
            Log.Error("Shell STA: failed to create the message windows", ex);
            _startError = ex;
            _ready.Set();
            DisposeWindows();
            _dispatcher?.InvokeShutdown();
            return;
        }

        _ready.Set();
        try
        {
            Dispatcher.Run();
        }
        catch (Exception ex)
        {
            Log.Error("Shell STA: dispatcher loop crashed", ex);
        }
        finally
        {
            DisposeWindows();
        }
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        var handlers = Volatile.Read(ref _handlers);
        foreach (var handler in handlers)
        {
            try
            {
                if (handler(hwnd, msg, wParam, lParam)) handled = true;
            }
            catch (Exception ex)
            {
                Log.Error($"Shell STA: message handler failed (msg 0x{msg:X4})", ex);
            }
        }
        return 0;
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Shell STA: unhandled exception", e.Exception);
        e.Handled = true;
    }

    private void Release()
    {
        bool stop;
        lock (Gate)
        {
            stop = --_refCount == 0;
            if (stop)
            {
                _stopping = true;
                if (ReferenceEquals(_current, this)) _current = null;
            }
        }
        if (stop) Stop();
    }

    private void Stop()
    {
        var dispatcher = _dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted) return;
        try
        {
            // Windows are destroyed by Run() after the loop exits, on their own thread.
            dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
            if (Thread.CurrentThread != _thread && _thread?.Join(TimeSpan.FromSeconds(2)) == false)
            {
                Log.Warn("Shell STA: thread did not stop within 2 s");
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Shell STA: shutdown failed", ex);
        }
    }

    private void DisposeWindows()
    {
        try { _messageWindow?.Dispose(); } catch (Exception ex) { Log.Warn("Shell STA: message window dispose failed", ex); }
        try { _broadcastWindow?.Dispose(); } catch (Exception ex) { Log.Warn("Shell STA: broadcast window dispose failed", ex); }
        _messageWindow = null;
        _broadcastWindow = null;
    }

    /// <summary>A reference on the shared STA thread. Dispose exactly once to release it.</summary>
    public sealed class Lease : IDisposable
    {
        private StaThread? _owner;

        internal Lease(StaThread owner) => _owner = owner;

        public StaThread Thread => _owner ?? throw new ObjectDisposedException(nameof(Lease));

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release();
    }

    private sealed class ActionDisposable(Action action) : IDisposable
    {
        private Action? _action = action;

        public void Dispose() => Interlocked.Exchange(ref _action, null)?.Invoke();
    }
}
