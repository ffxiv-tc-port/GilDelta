using System;
using System.Threading;
using System.Threading.Tasks;

namespace GilDelta.Storage;

/// <summary>
/// Last-write-wins background writer for one small file.
/// <see cref="Queue"/> is called from the framework thread with an <em>already
/// serialized</em> payload, so nothing is walked or serialized off-thread and no
/// lock is ever held across file I/O. At most one background write runs at a time;
/// payloads queued while a write is in flight replace each other, because for a
/// snapshot file only the newest one matters.
/// </summary>
public sealed class SerialJsonWriter
{
    private readonly string _path;
    private readonly Action<Exception>? _onError;
    private readonly Action? _onWritten;

    private string? _pending;
    private int _running;

    /// <param name="onWritten">
    /// Raised after every completed write attempt, on whichever thread did the write -
    /// including failed ones. It fires unconditionally on purpose: a caller that
    /// suspends appends while a rewrite is in flight would otherwise suspend them
    /// forever if the rewrite failed.
    /// </param>
    public SerialJsonWriter(string path, Action<Exception>? onError = null, Action? onWritten = null)
    {
        _path = path;
        _onError = onError;
        _onWritten = onWritten;
    }

    public string Path => _path;

    /// <summary>Schedules <paramref name="payload"/> to be written; returns immediately.</summary>
    public void Queue(string payload)
    {
        Interlocked.Exchange(ref _pending, payload);
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return;
        Task.Run(Drain);
    }

    /// <summary>
    /// Writes whatever is still queued, on the calling thread. Used from
    /// <c>Dispose()</c>, where a background task might not get to run at all.
    /// </summary>
    public void Flush()
    {
        var payload = Interlocked.Exchange(ref _pending, null);
        if (payload is not null) Write(payload);
    }

    private void Drain()
    {
        while (true)
        {
            var payload = Interlocked.Exchange(ref _pending, null);
            if (payload is null)
            {
                Volatile.Write(ref _running, 0);

                // A Queue() can land between the Exchange above and the flag reset;
                // that payload would otherwise sit unwritten forever because the
                // queueing thread saw _running == 1 and did not start a task.
                if (Volatile.Read(ref _pending) is null) return;
                if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return;
                continue;
            }

            Write(payload);
        }
    }

    private void Write(string payload)
    {
        try
        {
            AtomicFile.WriteAllText(_path, payload);
        }
        catch (Exception e)
        {
            _onError?.Invoke(e);
        }
        finally
        {
            try
            {
                _onWritten?.Invoke();
            }
            catch
            {
                // A completion handler must never take the writer down with it.
            }
        }
    }
}
