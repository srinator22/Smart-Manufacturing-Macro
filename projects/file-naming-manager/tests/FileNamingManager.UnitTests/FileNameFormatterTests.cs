// Purpose: Verify FileNameFormatter renders canonical names and rejects invalid combinations explicitly.
// Inputs: Naming tokens, descriptions, kinds, and roles, including malformed and boundary combinations.
// Outputs: Assertions on formatted strings and thrown exceptions.
// Dependencies: FileNamingManager.Core.
// Assumptions: None.
// Validation source: .work/TASK.md "Naming scheme" section; FileNameFormatterTests.

using FileNamingManager.Core;

namespace FileNamingManager.UnitTests;

public class FileNameFormatterTests
{
    private static readonly ProjectNumber Project124 = new(124);

    [Fact]
    public void FormatPartProducesCanonicalName()
    {
        NamingToken token = new(Project124, new ItemNumber(NumberSeries.Part, 2));

        string result = FileNameFormatter.Format(token, "Adaptor Plate Bottom", DocumentKind.Part, null);

        Assert.Equal("124-0002 Adaptor Plate Bottom.ipt", result);
    }

    [Fact]
    public void FormatSubAssemblyProducesCanonicalNameWithTag()
    {
        NamingToken token = new(Project124, new ItemNumber(NumberSeries.Assembly, 2));

        string result = FileNameFormatter.Format(token, "NDRM", DocumentKind.Assembly, AssemblyRole.Sub);

        Assert.Equal("124-A002 NDRM (sub-assembly).iam", result);
    }

    [Fact]
    public void FormatMainAssemblyProducesCanonicalNameWithTag()
    {
        NamingToken token = new(Project124, new ItemNumber(NumberSeries.Assembly, 1));

        string result = FileNameFormatter.Format(token, "Full Double Stack Mechanism", DocumentKind.Assembly, AssemblyRole.Main);

        Assert.Equal("124-A001 Full Double Stack Mechanism (main assembly).iam", result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void FormatEmptyDescriptionThrows(string description)
    {
        NamingToken token = new(Project124, new ItemNumber(NumberSeries.Part, 1));

        Assert.Throws<ArgumentException>(() => FileNameFormatter.Format(token, description, DocumentKind.Part, null));
    }

    [Fact]
    public void FormatAssemblyWithoutRoleThrows()
    {
        NamingToken token = new(Project124, new ItemNumber(NumberSeries.Assembly, 1));

        Assert.Throws<ArgumentException>(() => FileNameFormatter.Format(token, "Desc", DocumentKind.Assembly, null));
    }

    [Fact]
    public void FormatPartWithAssemblySeriesTokenThrows()
    {
        NamingToken token = new(Project124, new ItemNumber(NumberSeries.Assembly, 1));

        Assert.Throws<ArgumentException>(() => FileNameFormatter.Format(token, "Desc", DocumentKind.Part, null));
    }

    [Fact]
    public void FormatAssemblyWithPartSeriesTokenThrows()
    {
        NamingToken token = new(Project124, new ItemNumber(NumberSeries.Part, 1));

        Assert.Throws<ArgumentException>(() => FileNameFormatter.Format(token, "Desc", DocumentKind.Assembly, AssemblyRole.Main));
    }

    [Fact]
    public void FormatDrawingWithRoleMirrorsAssemblyTag()
    {
        NamingToken token = new(Project124, new ItemNumber(NumberSeries.Assembly, 1));

        string result = FileNameFormatter.Format(token, "Full Double Stack Mechanism", DocumentKind.Drawing, AssemblyRole.Main);

        Assert.Equal("124-A001 Full Double Stack Mechanism (main assembly).idw", result);
    }
}
