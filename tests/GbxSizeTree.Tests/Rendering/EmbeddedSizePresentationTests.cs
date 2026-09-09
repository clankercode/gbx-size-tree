using GbxSizeTree.Cli.Rendering;

namespace GbxSizeTree.Tests.Rendering;

public sealed class EmbeddedSizePresentationTests
{
    private readonly EmbeddedSizePresentation presentation = new();

    [Fact]
    public void FormatEntry_UsesNamedIndependentColumnsAndSignedRightMinusLeftDeltas()
    {
        var result = presentation.FormatEntry(
            new EmbeddedSizePresentation.EntrySize(20, 100),
            new EmbeddedSizePresentation.EntrySize(30, 120));

        Assert.Equal("20 → 30 (+10 B)", result.ZipBytes);
        Assert.Equal("100 → 120 (+20 B)", result.RawBytes);
        Assert.Equal("20.00% of raw → 25.00% of raw (+5.00 pp)", result.ZipToRawRatio);
        Assert.Equal("ZIP bytes", EmbeddedSizePresentation.ZipColumn);
        Assert.Equal("Raw bytes", EmbeddedSizePresentation.RawColumn);
        Assert.Equal("ZIP / raw", EmbeddedSizePresentation.RatioColumn);
        Assert.DoesNotContain("c|u", string.Join(' ', result.ZipBytes, result.RawBytes, result.ZipToRawRatio));
    }

    [Fact]
    public void FormatEntry_AddAndRemoveTreatAbsentBytesAsZeroWithoutInventingRatioDeltas()
    {
        var added = presentation.FormatEntry(null, new EmbeddedSizePresentation.EntrySize(10, 20));
        var removed = presentation.FormatEntry(new EmbeddedSizePresentation.EntrySize(30, 40), null);

        Assert.Equal("10 (+10 B)", added.ZipBytes);
        Assert.Equal("20 (+20 B)", added.RawBytes);
        Assert.Equal("50.00% of raw", added.ZipToRawRatio);
        Assert.Equal("30 (-30 B)", removed.ZipBytes);
        Assert.Equal("40 (-40 B)", removed.RawBytes);
        Assert.Equal("75.00% of raw", removed.ZipToRawRatio);
    }

    [Fact]
    public void FormatEntry_ZeroRawSizeMakesCompressionRatioExplicitlyUnavailable()
    {
        var empty = presentation.FormatEntry(null, new EmbeddedSizePresentation.EntrySize(0, 0));
        var becomesEmpty = presentation.FormatEntry(
            new EmbeddedSizePresentation.EntrySize(5, 10),
            new EmbeddedSizePresentation.EntrySize(0, 0));

        Assert.Equal("0 (0 B)", empty.ZipBytes);
        Assert.Equal("0 (0 B)", empty.RawBytes);
        Assert.Equal("unavailable", empty.ZipToRawRatio);
        Assert.Equal("50.00% of raw → unavailable", becomesEmpty.ZipToRawRatio);
        Assert.DoesNotContain("NaN", becomesEmpty.ZipToRawRatio);
        Assert.DoesNotContain("Infinity", becomesEmpty.ZipToRawRatio);
    }

    [Fact]
    public void FormatEntry_UnmeasuredSizesStayUnavailableAndDoNotProduceFalseDeltas()
    {
        var changed = presentation.FormatEntry(
            new EmbeddedSizePresentation.EntrySize(20, 100),
            new EmbeddedSizePresentation.EntrySize(null, 120));
        var unknown = presentation.FormatEntry(null, new EmbeddedSizePresentation.EntrySize(null, null));

        Assert.Equal("20 → unavailable", changed.ZipBytes);
        Assert.Equal("100 → 120 (+20 B)", changed.RawBytes);
        Assert.Equal("20.00% of raw → unavailable", changed.ZipToRawRatio);
        Assert.Equal("unavailable", unknown.ZipBytes);
        Assert.Equal("unavailable", unknown.RawBytes);
        Assert.Equal("unavailable", unknown.ZipToRawRatio);
    }

    [Fact]
    public void FormatMarginal_KeepsSignsAndDistinguishesUnavailableFromZero()
    {
        Assert.Equal("+12 B", presentation.FormatMarginal(12));
        Assert.Equal("-7 B", presentation.FormatMarginal(-7));
        Assert.Equal("0 B", presentation.FormatMarginal(0));
        Assert.Equal("unavailable: removal trial budget exhausted", presentation.FormatMarginal(null, "removal trial budget exhausted"));
        Assert.Equal("unavailable: not measured", presentation.FormatMarginal(null));
    }

    [Fact]
    public void NotesStateRatioDenominatorAndNonAdditiveOuterBaselineMeaning()
    {
        Assert.Contains("ZIP / raw", presentation.EntryNote);
        Assert.Contains("right minus left", presentation.EntryNote);

        var note = presentation.FormatOuterNote(123, null);

        Assert.Contains("left 123 B", note);
        Assert.Contains("right unavailable", note);
        Assert.Contains("signed baseline-minus-removal", note);
        Assert.Contains("non-additive", note);
        Assert.Contains("not an allocation of map size", note);
    }

    [Fact]
    public void FormattingUsesInvariantGroupingAndRejectsImpossibleNegativeEntrySizes()
    {
        var result = presentation.FormatEntry(
            new EmbeddedSizePresentation.EntrySize(1_000, 4_000),
            new EmbeddedSizePresentation.EntrySize(2_500, 5_000));

        Assert.Equal("1,000 → 2,500 (+1,500 B)", result.ZipBytes);
        Assert.Equal("25.00% of raw → 50.00% of raw (+25.00 pp)", result.ZipToRawRatio);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            presentation.FormatEntry(null, new EmbeddedSizePresentation.EntrySize(-1, 10)));
    }
}
