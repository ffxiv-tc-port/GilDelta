using System.Diagnostics;
using System.IO;
using System.Threading;
using GilDelta.Storage;

namespace GilDelta.Tests.Storage;

public class SerialJsonWriterTests : IDisposable
{
    private readonly string _dir;

    public SerialJsonWriterTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "GilDeltaTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }

    private string Path_ => Path.Combine(_dir, "state.json");

    private static bool WaitUntil(Func<bool> condition, int timeoutMs = 5000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            try
            {
                if (condition()) return true;
            }
            catch (IOException)
            {
                // Mid-rename; try again.
            }
            Thread.Sleep(10);
        }
        return false;
    }

    private static bool FileHas(string path, string expected)
    {
        if (!File.Exists(path)) return false;
        return File.ReadAllText(path) == expected;
    }

    [Fact]
    public void A_queued_payload_reaches_the_file()
    {
        var writer = new SerialJsonWriter(Path_);

        writer.Queue("""{"a":1}""");

        Assert.True(WaitUntil(() => FileHas(Path_, """{"a":1}""")));
    }

    [Fact]
    public void Flush_with_nothing_queued_does_not_create_a_file()
    {
        new SerialJsonWriter(Path_).Flush();

        Assert.False(File.Exists(Path_));
    }

    [Fact]
    public void The_last_queued_payload_wins()
    {
        // Only the newest state matters for a snapshot file, so a burst of updates
        // must collapse rather than queue up 50 writes.
        var writer = new SerialJsonWriter(Path_);

        for (var i = 0; i < 50; i++) writer.Queue($$"""{"n":{{i}}}""");

        Assert.True(WaitUntil(() => FileHas(Path_, """{"n":49}""")));
    }

    [Fact]
    public void Errors_are_reported_rather_than_thrown()
    {
        // A directory sitting where the file should go makes the write fail; the
        // framework thread must never see that exception.
        var path = Path.Combine(_dir, "blocked.json");
        Directory.CreateDirectory(path);

        Exception? reported = null;
        var writer = new SerialJsonWriter(path, e => reported = e);

        writer.Queue("""{"a":1}""");   // must not throw here
        writer.Flush();                // nor here

        Assert.True(WaitUntil(() => reported is not null));
    }

    [Fact]
    public void No_temp_files_are_left_behind()
    {
        var writer = new SerialJsonWriter(Path_);
        writer.Queue("""{"a":1}""");

        Assert.True(WaitUntil(() => FileHas(Path_, """{"a":1}""")));
        Assert.True(WaitUntil(() => Directory.GetFiles(_dir, "*.tmp").Length == 0));
    }
}
