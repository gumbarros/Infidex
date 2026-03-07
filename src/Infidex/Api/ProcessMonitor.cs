using System.Threading;

namespace Infidex.Api;

public sealed class ProcessMonitor : IDisposable
{
    private const int StartWaitGracePeriodMs = 100;
    private readonly object _sync = new();
    private ManualResetEventSlim _startedEvent = new(false);
    private ManualResetEventSlim _completedEvent = new(false);
    private CancellationTokenSource _cancellationTokenSource = new();
    private bool _disposed;
    private int _progressPercent;

    public event Action<int>? ProgressChanged;

    public bool IsRunning { get; private set; }

    public bool Succeeded { get; set; }

    public bool DidTimeOut { get; set; }

    public bool IsCompleted { get; private set; }

    public int TimeoutSeconds { get; set; } = -1;

    public ThreadPriority ThreadPriority { get; set; } = ThreadPriority.Normal;

    public string ErrorMessage { get; set; } = string.Empty;

    public Exception? Exception { get; set; }

    public DateTime StartTime { get; set; }

    public CancellationToken CancellationToken
    {
        get
        {
            ThrowIfDisposed();
            return _cancellationTokenSource.Token;
        }
    }

    public bool IsCancelled => !IsRunning && !Succeeded && !DidTimeOut && _cancellationTokenSource.IsCancellationRequested;

    public int ProgressPercent
    {
        get => _progressPercent;
        set
        {
            int clamped = Math.Clamp(value, 0, 100);
            Action<int>? handlers = null;

            lock (_sync)
            {
                if (_progressPercent == clamped)
                    return;

                _progressPercent = clamped;
                handlers = ProgressChanged;
            }

            if (handlers is null)
                return;

            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<int>)handler)(clamped);
                }
                catch
                {
                    // Monitoring callbacks must not break the monitored process.
                }
            }
        }
    }

    public void MarkStarted()
    {
        ThrowIfDisposed();

        lock (_sync)
        {
            IsRunning = true;
            IsCompleted = false;
            DidTimeOut = false;
            StartTime = DateTime.Now;
            _completedEvent.Reset();
            _startedEvent.Set();
        }
    }

    public void MarkFinished()
    {
        ThrowIfDisposed();

        lock (_sync)
        {
            IsRunning = false;
            IsCompleted = true;
            if (Succeeded)
                ProgressPercent = 100;
            _completedEvent.Set();
        }
    }

    public void Cancel()
    {
        ThrowIfDisposed();
        _cancellationTokenSource.Cancel();
    }

    public bool WaitForCompletion()
    {
        ThrowIfDisposed();

        if (IsCompleted)
            return true;

        if (!IsRunning)
        {
            // Allow a concurrently scheduled worker a brief window to call MarkStarted()
            // before treating the monitor as idle.
            bool started = _startedEvent.Wait(StartWaitGracePeriodMs);
            if (!started && !IsRunning && !IsCompleted)
                return true;
        }

        if (TimeoutSeconds > 0)
        {
            bool completed = _completedEvent.Wait(TimeSpan.FromSeconds(TimeoutSeconds));
            if (!completed)
            {
                DidTimeOut = true;
                Succeeded = false;
                ErrorMessage = "Process timed out.";
            }

            return completed;
        }

        _completedEvent.Wait();
        return true;
    }

    public Task<bool> WaitForCompletionAsync()
    {
        ThrowIfDisposed();

        if (!IsRunning)
            return Task.FromResult(true);

        return Task.Run(WaitForCompletion);
    }

    public void WaitForProcessStarted(int millisecondsTimeout)
    {
        ThrowIfDisposed();
        _startedEvent.Wait(millisecondsTimeout);
    }

    public void Reset()
    {
        ThrowIfDisposed();

        lock (_sync)
        {
            _progressPercent = 0;
            ErrorMessage = string.Empty;
            Exception = null;
            Succeeded = false;
            DidTimeOut = false;
            IsCompleted = false;

            CancellationTokenSource oldSource = _cancellationTokenSource;
            _cancellationTokenSource = new CancellationTokenSource();
            oldSource.Dispose();

            _completedEvent.Reset();
            if (!IsRunning)
                _startedEvent.Reset();
        }
    }

    public void ThrowIfOccupied()
    {
        ThrowIfDisposed();

        if (IsRunning)
            throw new InvalidOperationException("A process is already running.");
    }

    public static bool ShouldAbort(ProcessMonitor? monitor)
    {
        if (monitor is null)
            return false;

        if (monitor._disposed)
            return true;

        if (monitor.TimeoutSeconds > 0 && monitor.IsRunning && monitor.StartTime != default)
        {
            if (DateTime.Now - monitor.StartTime >= TimeSpan.FromSeconds(monitor.TimeoutSeconds))
            {
                monitor.DidTimeOut = true;
                monitor.Succeeded = false;
                monitor.ErrorMessage = "Process timed out.";
                return true;
            }
        }

        if (monitor._cancellationTokenSource.IsCancellationRequested)
        {
            monitor.Succeeded = false;
            if (string.IsNullOrEmpty(monitor.ErrorMessage))
                monitor.ErrorMessage = "Process was cancelled.";
            return true;
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _cancellationTokenSource.Dispose();
        _startedEvent.Dispose();
        _completedEvent.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ProcessMonitor));
    }
}
