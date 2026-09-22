// Purpose: Verify ProjectNumber and ItemNumber boundary validation, parsing, and formatting.
// Inputs: Boundary, negative, and malformed integers and strings.
// Outputs: Assertions on constructed values, thrown exceptions, and ToString output.
// Dependencies: FileNamingManager.Core.
// Assumptions: None.
// Validation source: .work/TASK.md "Naming scheme" section (PPP 100-999, NNNN 0001-9999, ANNN A001-A999).

using System.Globalization;
using FileNamingManager.Core;

namespace FileNamingManager.UnitTests;

public class NamingModelsTests
{
    [Theory]
    [InlineData(100)]
    [InlineData(999)]
    [InlineData(124)]
    public void ProjectNumberWithinRangeConstructs(int value)
    {
        ProjectNumber project = new(value);

        Assert.Equal(value, project.Value);
    }

    [Theory]
    [InlineData(99)]
    [InlineData(1000)]
    [InlineData(0)]
    [InlineData(-1)]
    public void ProjectNumberOutOfRangeThrowsWithActualValueInMessage(int value)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() => new ProjectNumber(value));

        Assert.Contains(value.ToString(CultureInfo.InvariantCulture), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectNumberToStringIsThreeDigits()
    {
        Assert.Equal("124", new ProjectNumber(124).ToString());
    }

    [Theory]
    [InlineData("124", true, 124)]
    [InlineData("999", true, 999)]
    [InlineData("099", false, 0)]
    [InlineData("12", false, 0)]
    [InlineData("12a", false, 0)]
    [InlineData("", false, 0)]
    [InlineData(null, false, 0)]
    public void ProjectNumberTryParseHandlesValidAndInvalidInput(string? text, bool expectedSuccess, int expectedValue)
    {
        bool success = ProjectNumber.TryParse(text, out ProjectNumber result);

        Assert.Equal(expectedSuccess, success);
        if (expectedSuccess)
        {
            Assert.Equal(expectedValue, result.Value);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(9999)]
    public void ItemNumberPartWithinRangeFormatsFourDigits(int value)
    {
        ItemNumber item = new(NumberSeries.Part, value);

        Assert.Equal(value.ToString("D4", CultureInfo.InvariantCulture), item.ToString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10000)]
    public void ItemNumberPartOutOfRangeThrows(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ItemNumber(NumberSeries.Part, value));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(999)]
    public void ItemNumberAssemblyWithinRangeFormatsWithAPrefix(int value)
    {
        ItemNumber item = new(NumberSeries.Assembly, value);

        Assert.Equal("A" + value.ToString("D3", CultureInfo.InvariantCulture), item.ToString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1000)]
    public void ItemNumberAssemblyOutOfRangeThrows(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ItemNumber(NumberSeries.Assembly, value));
    }

    [Fact]
    public void NamingTokenToStringFormatsPartAndAssembly()
    {
        NamingToken partToken = new(new ProjectNumber(124), new ItemNumber(NumberSeries.Part, 2));
        NamingToken assemblyToken = new(new ProjectNumber(124), new ItemNumber(NumberSeries.Assembly, 2));

        Assert.Equal("124-0002", partToken.ToString());
        Assert.Equal("124-A002", assemblyToken.ToString());
    }

    [Theory]
    [InlineData("ipt", DocumentKind.Part)]
    [InlineData(".IPT", DocumentKind.Part)]
    [InlineData("iam", DocumentKind.Assembly)]
    [InlineData("idw", DocumentKind.Drawing)]
    [InlineData("dwg", DocumentKind.Drawing)]
    [InlineData("ipn", DocumentKind.Presentation)]
    [InlineData("dwf", DocumentKind.Other)]
    [InlineData("", DocumentKind.Other)]
    public void DocumentKindFromExtensionIsCaseInsensitive(string extension, DocumentKind expected)
    {
        Assert.Equal(expected, DocumentKindExtensions.FromExtension(extension));
    }
}
