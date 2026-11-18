using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace GilDelta.Storage;

/// <summary>
/// Newtonsoft text of a value, written into one buffer that is reused across calls.
/// Contract: the characters are those of <c>JsonConvert.SerializeObject(value)</c> - a serializer
/// from <see cref="JsonSerializer.CreateDefault()"/> (so <c>JsonConvert.DefaultSettings</c> applies
/// exactly as it does there), an invariant-culture writer and the serializer's own formatting.
/// The returned builder is overwritten by the next call. Not thread-safe; one caller thread.
/// </summary>
public sealed class ReusedJsonText
{
    private readonly StringBuilder _text = new(1024);

    public StringBuilder Write(object value)
    {
        _text.Clear();
        var serializer = JsonSerializer.CreateDefault();
        using var writer = new JsonTextWriter(new StringWriter(_text, CultureInfo.InvariantCulture));
        writer.Formatting = serializer.Formatting;
        serializer.Serialize(writer, value);
        return _text;
    }
}
