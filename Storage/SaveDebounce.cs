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
}
