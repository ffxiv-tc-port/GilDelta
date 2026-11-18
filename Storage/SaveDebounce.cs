using System;
using System.Text;

namespace GilDelta.Storage;

/// <summary>
/// Decides when a periodically sampled serialized state should be saved: only once it
/// differs from what was last saved AND two samples in a row agree, so a value that is
/// still moving (a window being dragged) is written once it settles. The first sample
/// is the baseline and is never saved. Not thread-safe; one caller thread.
/// </summary>
public sealed class SaveDebounce
{
    private string? _saved;
    private string? _pending;

    /// <returns>true when <paramref name="current"/> should be saved now; it then counts as saved.</returns>
    public bool ShouldSave(string current)
    {
        if (_saved is null || current == _saved)
        {
            _saved ??= current;
            _pending = null;
            return false;
        }

        if (current != _pending)
        {
            _pending = current;
            return false;
        }

        // Counted as saved before the caller writes: a failed write is retried on the
        // next change, not on every sample.
        _saved = current;
        _pending = null;
        return true;
    }

    /// <summary>
    /// Same decisions as <see cref="ShouldSave(string)"/> with <c>current.ToString()</c>, but a
    /// sample equal to what is already saved or pending allocates nothing.
    /// </summary>
    public bool ShouldSave(StringBuilder current)
    {
        if (_saved is null || current.Equals(_saved.AsSpan()))
        {
            _saved ??= current.ToString();
            _pending = null;
            return false;
        }

        if (_pending is null || !current.Equals(_pending.AsSpan()))
        {
            _pending = current.ToString();
            return false;
        }

        _saved = _pending;
        _pending = null;
        return true;
    }
}
