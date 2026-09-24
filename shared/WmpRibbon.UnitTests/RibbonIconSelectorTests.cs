using WmpRibbon;

namespace WmpRibbon.UnitTests;

public sealed class RibbonIconSelectorTests
{
    [Theory]
    [InlineData("LightTheme", RibbonTheme.Light)]
    [InlineData("Light Theme", RibbonTheme.Light)]
    [InlineData("light", RibbonTheme.Light)]
    [InlineData("DarkTheme", RibbonTheme.Dark)]
    [InlineData("Dark Theme", RibbonTheme.Dark)]
    [InlineData("HighContrast", RibbonTheme.Dark)]
    [InlineData("", RibbonTheme.Dark)]
    [InlineData(null, RibbonTheme.Dark)]
    public void ThemeFromNameMapsOnlyLightNamesToLight(string? themeName, RibbonTheme expected) =>
        Assert.Equal(expected, RibbonIconSelector.ThemeFromName(themeName));

    [Theory]
    [InlineData(-1, 16, 32)]
    [InlineData(0, 16, 32)]
    [InlineData(72, 16, 32)]
    [InlineData(96, 16, 32)]
    [InlineData(97, 20, 40)]
    [InlineData(120, 20, 40)]
    [InlineData(121, 24, 48)]
    [InlineData(144, 24, 48)]
    [InlineData(168, 32, 64)]
    [InlineData(192, 32, 64)]
    [InlineData(288, 32, 64)]
    public void SizesAreTheSmallestRenderedSizeAtOrAboveTheScaledTarget(int dpi, int small, int large)
    {
        Assert.Equal(small, RibbonIconSelector.SmallSize(dpi));
        Assert.Equal(large, RibbonIconSelector.LargeSize(dpi));
    }

    [Fact]
    public void ResourceNameCombinesPrefixIconThemeAndSize() =>
        Assert.Equal(
            "SmartManufacturingExporter.AddIn.Ribbon.smart-export-light-40.png",
            RibbonIconSelector.ResourceName("SmartManufacturingExporter.AddIn", "smart-export", RibbonTheme.Light, 40));

    [Fact]
    public void FileNameUsesTheDarkSuffixForTheDarkTheme() =>
        Assert.Equal("check-updates-dark-16.png", RibbonIconSelector.FileName("check-updates", RibbonTheme.Dark, 16));

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(28)]
    [InlineData(128)]
    public void FileNameRejectsSizesThatAreNeverRendered(int size) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => RibbonIconSelector.FileName("smart-export", RibbonTheme.Dark, size));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void FileNameRejectsBlankIconNames(string iconName) =>
        Assert.Throws<ArgumentException>(() => RibbonIconSelector.FileName(iconName, RibbonTheme.Dark, 16));

    [Fact]
    public void FileNameRejectsNullIconNames() =>
        Assert.Throws<ArgumentNullException>(() => RibbonIconSelector.FileName(null!, RibbonTheme.Dark, 16));

    [Fact]
    public void ResourceNameRejectsBlankPrefixes() =>
        Assert.Throws<ArgumentException>(() => RibbonIconSelector.ResourceName(" ", "smart-export", RibbonTheme.Dark, 16));

    [Fact]
    public void ThemeSuffixRejectsUndefinedThemes() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => RibbonIconSelector.ThemeSuffix((RibbonTheme)7));

    [Fact]
    public void EverySelectableSizeIsARenderedSize()
    {
        foreach (int dpi in Enumerable.Range(0, 400))
        {
            Assert.Contains(RibbonIconSelector.SmallSize(dpi), RibbonIconSelector.SmallSizes);
            Assert.Contains(RibbonIconSelector.LargeSize(dpi), RibbonIconSelector.LargeSizes);
        }
    }
}
