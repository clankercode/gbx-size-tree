using GbxSizeTree.Cli.Rendering;

namespace GbxSizeTree.Tests.Rendering;

public sealed class TmTextTests
{
    [Theory]
    [InlineData("Plain Name", "Plain Name")]
    [InlineData("$f00Red$g Map", "Red Map")]
    [InlineData("$o$iStyled$z End", "Styled End")]
    [InlineData("$00fA$0f0B$abc", "AB")]
    [InlineData("$wWide $nNarrow $mNormal", "Wide Narrow Normal")]
    [InlineData("$$50 map", "$50 map")]
    [InlineData("$l[https://xk.io]link text$l after", "link text after")]
    [InlineData("$h[maniaplanet:link]inner$h", "inner")]
    [InlineData("truncated color $f0", "truncated color ")]
    [InlineData("trailing dollar $", "trailing dollar $")]
    [InlineData("", "")]
    public void Deformat_StripsFormatCodes(string input, string expected)
    {
        Assert.Equal(expected, TmText.Deformat(input));
    }
}
