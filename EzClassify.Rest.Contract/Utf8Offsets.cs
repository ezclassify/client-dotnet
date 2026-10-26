using System;

namespace EzClassify;

/// <summary>Conversion helpers for the UTF-8 byte offsets used by document APIs.</summary>
/// <remarks>
/// Each call validates and walks the text on its own. Callers converting more
/// than one offset over the same document should build a
/// <see cref="Utf8OffsetMap"/> instead, which validates once and resumes from
/// the previous position.
/// </remarks>
public static class Utf8Offsets
{
    /// <summary>
    /// Convert a zero-based UTF-8 byte offset to a zero-based .NET string index.
    /// The offset must fall on a Unicode scalar boundary; unpaired UTF-16
    /// surrogates and out-of-range offsets are rejected.
    /// </summary>
    public static int ByteOffsetToStringIndex(string text, int byteOffset)
        => new Utf8OffsetMap(text).ByteOffsetToStringIndex(byteOffset);

    /// <summary>Convert an end-exclusive UTF-8 byte range to .NET string indices.</summary>
    public static (int Start, int End) ByteRangeToStringRange(string text, int start, int end)
        => new Utf8OffsetMap(text).ByteRangeToStringRange(start, end);
}

/// <summary>
/// Reusable UTF-8 byte offset to .NET string index converter for one document.
/// </summary>
/// <remarks>
/// A single audit report carries UTF-8 offsets for every part, unmarked span,
/// scan match, and evidence span it reports. Resolving each of those through
/// <see cref="Utf8Offsets"/> revalidates and rewalks the whole document every
/// time, which is quadratic in document length over one report. This type
/// validates the text once at construction and remembers the last position it
/// resolved, so offsets requested in ascending order cost one forward pass over
/// the document in total. An offset behind the remembered position still
/// resolves correctly, by rescanning from the start.
/// <para>Instances are not thread-safe: the remembered position is mutable
/// state. Use one instance per document per thread.</para>
/// </remarks>
public sealed class Utf8OffsetMap
{
    private readonly string _text;
    private int _index;
    private int _bytes;

    /// <summary>Build a converter for <paramref name="text"/>.</summary>
    /// <param name="text">Document text the offsets refer to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="text"/> contains an unpaired UTF-16 surrogate.</exception>
    public Utf8OffsetMap(string text)
    {
        if (text is null) throw new ArgumentNullException(nameof(text));
        ValidateUtf16(text);
        _text = text;
    }

    /// <summary>
    /// Convert a zero-based UTF-8 byte offset to a zero-based .NET string index.
    /// The offset must fall on a Unicode scalar boundary.
    /// </summary>
    /// <param name="byteOffset">End-exclusive UTF-8 byte offset into the document.</param>
    /// <returns>The .NET string index the offset lands on.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="byteOffset"/> is negative or past the end of the document.</exception>
    /// <exception cref="ArgumentException"><paramref name="byteOffset"/> does not fall on a UTF-8 scalar boundary.</exception>
    public int ByteOffsetToStringIndex(int byteOffset)
    {
        if (byteOffset < 0) throw new ArgumentOutOfRangeException(nameof(byteOffset));

        // Resume from the last resolved position when the caller is moving
        // forward, which is how a report's spans arrive; otherwise start over.
        var fromIndex = byteOffset >= _bytes ? _index : 0;
        var fromBytes = byteOffset >= _bytes ? _bytes : 0;

        var index = ScanToByteOffset(_text, byteOffset, fromIndex, fromBytes, nameof(byteOffset));
        _index = index;
        _bytes = byteOffset;
        return index;
    }

    /// <summary>Convert an end-exclusive UTF-8 byte range to .NET string indices.</summary>
    /// <param name="start">Start byte offset, inclusive.</param>
    /// <param name="end">End byte offset, exclusive.</param>
    /// <returns>The .NET string indices the range maps to.</returns>
    /// <exception cref="ArgumentOutOfRangeException">An offset is negative or past the end of the document.</exception>
    /// <exception cref="ArgumentException">The range is inverted, or an offset does not fall on a UTF-8 scalar boundary.</exception>
    public (int Start, int End) ByteRangeToStringRange(int start, int end)
    {
        if (end < start) throw new ArgumentException("end must be greater than or equal to start", nameof(end));
        if (start < 0) throw new ArgumentOutOfRangeException(nameof(start));

        var startIndex = ByteOffsetToStringIndex(start);
        var endIndex = ByteOffsetToStringIndex(end);
        return (startIndex, endIndex);
    }

    // Walk forward from a known (string index, byte offset) pair.
    private static int ScanToByteOffset(string text, int byteOffset, int fromIndex, int fromBytes, string parameterName)
    {
        var bytes = fromBytes;
        for (var i = fromIndex; i < text.Length; i++)
        {
            if (bytes == byteOffset) return i;
            var ch = text[i];
            if (char.IsHighSurrogate(ch))
            {
                bytes += 4;
                i++;
            }
            else if (char.IsLowSurrogate(ch)) throw new InvalidOperationException("validated surrogate state changed");
            else if (ch <= 0x7f) bytes += 1;
            else if (ch <= 0x7ff) bytes += 2;
            else bytes += 3;

            if (bytes > byteOffset)
                throw new ArgumentException("byteOffset does not fall on a UTF-8 scalar boundary", parameterName);
        }

        if (bytes == byteOffset) return text.Length;
        throw new ArgumentOutOfRangeException(parameterName);
    }

    private static void ValidateUtf16(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]))
            {
                if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1]))
                    throw new ArgumentException("text contains an unpaired UTF-16 surrogate", nameof(text));
                i++;
            }
            else if (char.IsLowSurrogate(text[i]))
            {
                throw new ArgumentException("text contains an unpaired UTF-16 surrogate", nameof(text));
            }
        }
    }
}
