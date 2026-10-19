using System;
using System.IO;

namespace GilDelta.Storage;

/// <summary>
/// Small-file writes that cannot leave a half-written file behind.
/// </summary>
public static class AtomicFile
{
    /// <summary>
    /// Writes <paramref name="contents"/> to <paramref name="path"/> through a
    /// uniquely-named temp file plus a rename.
    /// The temp name carries a GUID on purpose: two writers racing on the same
    /// target must not collide on the same scratch file. The extension chain ends
    /// in <c>.tmp</c> so directory scans that look for <c>*.jsonl</c> / <c>*.json</c>
    /// never pick a temp file up.
    /// </summary>
    public static void WriteAllText(string path, string contents)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temp, contents);

            if (File.Exists(path))
                File.Replace(temp, path, destinationBackupFileName: null);
            else
                File.Move(temp, path);
        }
        finally
        {
            // File.Replace / File.Move consume the temp on success; on any failure
            // path it is still sitting there and would accumulate forever.
            try
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
            catch
            {
                // Best effort — a leftover temp is not worth masking the real error.
            }
        }
    }
}
