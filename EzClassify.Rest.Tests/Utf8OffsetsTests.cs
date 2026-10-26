using System;
using Xunit;

namespace EzClassify.Tests;

/// <summary>
/// Byte-offset conversion contract. ByteRangeToStringRange resolves both ends in
/// a single validated pass, so it has to agree exactly with two independent
/// ByteOffsetToStringIndex calls, including on the rejection paths.
/// </summary>
public class Utf8OffsetsTests
{
    // Ascii, two-byte, three-byte, and a surrogate pair (four bytes).
    private const string Mixed = "abé€X\U0001F600yz";

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(4, 7)]
    [InlineData(0, 14)]
    [InlineData(8, 12)]
    [InlineData(14, 14)]
    public void ByteRange_MatchesTwoSingleOffsetConversions(int start, int end)
    {
        var expected = (
            Utf8Offsets.ByteOffsetToStringIndex(Mixed, start),
            Utf8Offsets.ByteOffsetToStringIndex(Mixed, end));

        Assert.Equal(expected, Utf8Offsets.ByteRangeToStringRange(Mixed, start, end));
    }

    [Fact]
    public void ByteRange_EndInsideAScalar_IsRejected()
    {
        // Byte 9 lands in the middle of the four-byte scalar that starts at 8.
        Assert.Throws<ArgumentException>(() => Utf8Offsets.ByteRangeToStringRange(Mixed, 0, 9));
    }

    [Fact]
    public void ByteRange_StartInsideAScalar_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => Utf8Offsets.ByteRangeToStringRange(Mixed, 3, 7));
    }

    [Fact]
    public void ByteRange_EndPastTheText_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Utf8Offsets.ByteRangeToStringRange(Mixed, 0, 99));
    }

    [Fact]
    public void ByteRange_NegativeStart_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Utf8Offsets.ByteRangeToStringRange(Mixed, -1, 4));
    }

    [Fact]
    public void ByteRange_EndBeforeStart_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => Utf8Offsets.ByteRangeToStringRange(Mixed, 4, 2));
    }

    [Fact]
    public void ByteRange_NullText_IsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => Utf8Offsets.ByteRangeToStringRange(null!, 0, 0));
    }

    [Fact]
    public void ByteRange_UnpairedSurrogate_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => Utf8Offsets.ByteRangeToStringRange("a\ud83d", 0, 1));
    }
}

/// <summary>
/// Utf8OffsetMap contract. The map exists so one document's many spans cost one
/// forward pass instead of one pass per offset, so the cases that matter are:
/// it agrees with the one-shot helpers everywhere, ascending offsets resolve
/// correctly while the position is being carried forward, and an offset behind
/// the carried position still resolves correctly.
/// </summary>
public class Utf8OffsetMapTests
{
    private const string Mixed = "abé€X\U0001F600yz";

    [Fact]
    public void Constructor_NullText_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new Utf8OffsetMap(null!));
    }

    [Fact]
    public void Constructor_UnpairedSurrogate_Throws()
    {
        Assert.Throws<ArgumentException>(() => new Utf8OffsetMap("a\ud83dz"));
    }

    [Fact]
    public void ByteOffsetToStringIndex_AscendingOffsets_AgreeWithTheOneShotHelper()
    {
        var map = new Utf8OffsetMap(Mixed);

        for (var byteOffset = 0; byteOffset <= 15; byteOffset++)
        {
            int expected;
            try
            {
                expected = Utf8Offsets.ByteOffsetToStringIndex(Mixed, byteOffset);
            }
            catch (ArgumentException)
            {
                Assert.ThrowsAny<ArgumentException>(() => map.ByteOffsetToStringIndex(byteOffset));
                continue;
            }

            Assert.Equal(expected, map.ByteOffsetToStringIndex(byteOffset));
        }
    }

    [Fact]
    public void ByteOffsetToStringIndex_DescendingOffsets_StillResolve()
    {
        var map = new Utf8OffsetMap(Mixed);

        Assert.Equal(Mixed.Length, map.ByteOffsetToStringIndex(14));
        Assert.Equal(0, map.ByteOffsetToStringIndex(0));
        Assert.Equal(2, map.ByteOffsetToStringIndex(2));
    }

    [Fact]
    public void ByteOffsetToStringIndex_RejectedOffset_LeavesTheMapUsable()
    {
        var map = new Utf8OffsetMap(Mixed);
        Assert.Equal(2, map.ByteOffsetToStringIndex(2));

        // Byte 3 is inside the two-byte encoding of "é".
        Assert.Throws<ArgumentException>(() => map.ByteOffsetToStringIndex(3));

        Assert.Equal(3, map.ByteOffsetToStringIndex(4));
    }

    [Fact]
    public void ByteRangeToStringRange_AgreesWithTheOneShotHelper()
    {
        var map = new Utf8OffsetMap(Mixed);

        Assert.Equal(Utf8Offsets.ByteRangeToStringRange(Mixed, 2, 7), map.ByteRangeToStringRange(2, 7));
        Assert.Equal(Utf8Offsets.ByteRangeToStringRange(Mixed, 0, 14), map.ByteRangeToStringRange(0, 14));
    }

    [Fact]
    public void ByteRangeToStringRange_InvertedRange_Throws()
    {
        var map = new Utf8OffsetMap(Mixed);

        Assert.Throws<ArgumentException>(() => map.ByteRangeToStringRange(7, 2));
    }

    [Fact]
    public void ByteOffsetToStringIndex_ManySpansOverALongDocument_ResolveCorrectly()
    {
        // The reason the type exists: a report's worth of ascending spans over a
        // long document. Every one still has to land where the one-shot helper
        // would put it.
        var text = string.Concat(System.Linq.Enumerable.Repeat("héllo wörld ", 20000));
        var map = new Utf8OffsetMap(text);

        for (var i = 0; i < 200; i++)
        {
            var byteOffset = i * 14 * 40;
            Assert.Equal(Utf8Offsets.ByteOffsetToStringIndex(text, byteOffset), map.ByteOffsetToStringIndex(byteOffset));
        }
    }
}
