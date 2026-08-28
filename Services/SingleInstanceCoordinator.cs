using System;
using System.Threading;

namespace BdoClient.Services;

/// <summary>
/// Coordinates a single primary normal BDO UA Client instance per Windows session.
/// A named kernel Mutex grants ownership to the first normal process; a named
/// auto-reset EventWaitHandle lets a secondary process request activation of the
/// primary. The self-update helper must never use this coordinator.
/// </summary>
public sealed class SingleInstanceCoordinator : IDisposable
{
    private const string DefaultMutexName = @"Local\BDO-UA-Client.SingleInstance";
    private const string DefaultActivationEventName = @"Local\BDO-UA-Client.Activate";

    private readonly Mutex _mutex;
    private readonly bool _ownsMutex;
    private readonly EventWaitHandle _activationEvent;

    private Thread? _waitThread;
    private volatile bool _disposed;
    private volatile bool _pendingSignal;
    private Action? _activationCallback;
    private readonly object _callbackLock = new();

    public bool IsPrimary { get; }

    public SingleInstanceCoordinator()
        : this(DefaultMutexName, DefaultActivationEventName)
    {
    }

    // Testable constructor with custom names so tests use unique kernel objects
    // and never touch production named objects.
    internal SingleInstanceCoordinator(string mutexName, string activationEventName)
    {
        if (string.IsNullOrWhiteSpace(mutexName))
            throw new ArgumentNullException(nameof(mutexName));
        if (string.IsNullOrWhiteSpace(activationEventName))
            throw new ArgumentNullException(nameof(activationEventName));

        // Open/create the activation event early so a secondary can signal even before
        // the primary has registered its callback.
        _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, activationEventName);

        // Atomic acquire: createdNew == true means no other instance owned the mutex.
        bool createdNew;
        _mutex = new Mutex(initiallyOwned: true, mutexName, out createdNew);
        _ownsMutex = createdNew;
        IsPrimary = createdNew;

        if (IsPrimary)
        {
            _waitThread = new Thread(WaitForActivationLoop)
            {
                IsBackground = true,
                Name = "SingleInstanceActivation"
            };
            _waitThread.Start();
        }
    }

    public void SignalActivation()
    {
        if (_disposed) return;
        try
        {
            _activationEvent.Set();
        }
        catch (ObjectDisposedException)
        {
            // best-effort signaling
        }
    }

    public void RegisterActivationCallback(Action callback)
    {
        if (callback == null) throw new ArgumentNullException(nameof(callback));
        if (!IsPrimary) return;

        Action? toInvoke = null;
        lock (_callbackLock)
        {
            _activationCallback = callback;
            if (_pendingSignal)
            {
                _pendingSignal = false;
                toInvoke = callback;
            }
        }

        // An early signal that arrived before registration is replayed exactly once.
        toInvoke?.Invoke();
    }

    private void WaitForActivationLoop()
    {
        while (!_disposed)
        {
            // Bounded wait so disposal can wake the thread without an infinite hang.
            if (_activationEvent.WaitOne(500))
            {
                if (_disposed) break;

                Action? cb;
                lock (_callbackLock)
                {
                    cb = _activationCallback;
                    if (cb == null)
                        _pendingSignal = true;
                }

                cb?.Invoke();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Only the primary owns a wait thread; wake it via the shared event.
        if (IsPrimary)
        {
            try
            {
                _activationEvent.Set();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        if (_waitThread != null)
        {
            if (_waitThread != Thread.CurrentThread)
                _waitThread.Join(TimeSpan.FromSeconds(2));
            _waitThread = null;
        }

        lock (_callbackLock)
        {
            _activationCallback = null;
        }

        if (_ownsMutex)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Not owned by this thread; ignore.
            }
            catch (ObjectDisposedException)
            {
            }
        }

        _mutex.Dispose();
        _activationEvent.Dispose();
    }
}
