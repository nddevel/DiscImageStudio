// GLib main-loop implementations of the WPF dispatcher types used by the shared view models.

namespace System.Windows.Threading;

/// <summary>Subset of WPF's priorities; each maps onto a GLib source priority.</summary>
public enum DispatcherPriority
{
    Invalid = -1,
    Inactive = 0,
    SystemIdle = 1,
    ApplicationIdle = 2,
    ContextIdle = 3,
    Background = 4,
    Input = 5,
    Loaded = 6,
    Render = 7,
    DataBind = 8,
    Normal = 9,
    Send = 10,
}

/// <summary>The UI-thread dispatcher. There is exactly one: the GLib default main context,
/// owned by the thread that created it (the GTK main thread).</summary>
public sealed class Dispatcher
{
    private static Dispatcher? _main;

    private Dispatcher(Thread thread)
    {
        Thread = thread;
    }

    /// <summary>The GTK main-thread dispatcher. Must first be touched from the main thread.</summary>
    public static Dispatcher CurrentDispatcher => _main ??= new Dispatcher(Thread.CurrentThread);

    public Thread Thread { get; }

    public bool CheckAccess() => Thread.CurrentThread == Thread;

    public void VerifyAccess()
    {
        if (!CheckAccess())
            throw new InvalidOperationException("The calling thread cannot access this object because a different thread owns it.");
    }

    public void Invoke(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (CheckAccess())
        {
            callback();
            return;
        }

        InvokeAsync(callback).GetAwaiter().GetResult();
    }

    public T Invoke<T>(Func<T> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (CheckAccess()) return callback();
        return InvokeAsync(callback).GetAwaiter().GetResult();
    }

    public Task InvokeAsync(Action callback, DispatcherPriority priority = DispatcherPriority.Normal)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return InvokeAsync(() =>
        {
            callback();
            return true;
        }, priority);
    }

    public Task<T> InvokeAsync<T>(Func<T> callback, DispatcherPriority priority = DispatcherPriority.Normal)
    {
        ArgumentNullException.ThrowIfNull(callback);
        TaskCompletionSource<T> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            try
            {
                completion.SetResult(callback());
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        }, priority);
        return completion.Task;
    }

    public void BeginInvoke(Action callback) => Post(callback, DispatcherPriority.Normal);

    public void BeginInvoke(DispatcherPriority priority, Delegate method) =>
        Post(() => method.DynamicInvoke(), priority);

    public void BeginInvoke(Delegate method, params object?[] arguments) =>
        Post(() => method.DynamicInvoke(arguments), DispatcherPriority.Normal);

    /// <summary>Queues work on the main loop; safe from any thread.</summary>
    public static void Post(Action callback, DispatcherPriority priority = DispatcherPriority.Normal)
    {
        ArgumentNullException.ThrowIfNull(callback);
        GLib.Functions.IdleAdd(ToGLibPriority(priority), () =>
        {
            callback();
            return false;
        });
    }

    internal static int ToGLibPriority(DispatcherPriority priority) => priority switch
    {
        DispatcherPriority.Send or DispatcherPriority.Normal or DispatcherPriority.DataBind
            or DispatcherPriority.Input => GLib.Constants.PRIORITY_DEFAULT,
        // GTK lays out at HIGH_IDLE + 10 and paints at HIGH_IDLE + 20; Loaded/Render work
        // runs after both, as in WPF.
        DispatcherPriority.Render or DispatcherPriority.Loaded => GLib.Constants.PRIORITY_HIGH_IDLE + 30,
        _ => GLib.Constants.PRIORITY_DEFAULT_IDLE,
    };
}

/// <summary>Repeating main-loop timer with WPF semantics: Start on a running timer is a
/// no-op, changing Interval restarts the countdown, and Tick repeats until Stop.</summary>
public sealed class DispatcherTimer
{
    private readonly DispatcherPriority _priority;
    private TimeSpan _interval;
    private uint _sourceId;

    public DispatcherTimer()
        : this(DispatcherPriority.Background)
    {
    }

    public DispatcherTimer(DispatcherPriority priority)
    {
        _priority = priority;
    }

    public DispatcherTimer(DispatcherPriority priority, Dispatcher dispatcher)
        : this(priority)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
    }

    public DispatcherTimer(TimeSpan interval, DispatcherPriority priority, EventHandler callback, Dispatcher dispatcher)
        : this(priority, dispatcher)
    {
        _interval = interval;
        Tick += callback;
        Start();
    }

    public event EventHandler? Tick;

    public bool IsEnabled { get; private set; }

    public TimeSpan Interval
    {
        get => _interval;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero);
            _interval = value;
            if (IsEnabled) Schedule();
        }
    }

    public object? Tag { get; set; }

    public void Start()
    {
        if (IsEnabled) return;
        IsEnabled = true;
        Schedule();
    }

    public void Stop()
    {
        IsEnabled = false;
        Unschedule();
    }

    private void Schedule()
    {
        Unschedule();
        uint milliseconds = (uint)Math.Clamp(_interval.TotalMilliseconds, 0, uint.MaxValue);
        uint id = 0;
        id = GLib.Functions.TimeoutAdd(Dispatcher.ToGLibPriority(_priority), milliseconds, () =>
        {
            if (!IsEnabled || _sourceId != id) return false;
            Tick?.Invoke(this, EventArgs.Empty);
            // Tick may have stopped or restarted the timer; a restart owns a new source.
            bool keep = IsEnabled && _sourceId == id;
            if (!keep && _sourceId == id) _sourceId = 0;
            return keep;
        });
        _sourceId = id;
    }

    private void Unschedule()
    {
        if (_sourceId == 0) return;
        uint id = _sourceId;
        _sourceId = 0;
        GLib.Functions.SourceRemove(id);
    }
}
