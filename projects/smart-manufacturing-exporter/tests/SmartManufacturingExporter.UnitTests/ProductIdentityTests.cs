using SmartManufacturingExporter.Core;

namespace SmartManufacturingExporter.UnitTests;

public sealed class ProductIdentityTests
{
    [Fact]
    public void CompatibilityBoundaryTargetsOnlyInventor2027()
    {
        Assert.Equal("Smart Manufacturing Exporter", ProductIdentity.ProductName);
        Assert.Equal(2027, ProductIdentity.SupportedInventorReleaseYear);
        Assert.Equal(31, ProductIdentity.SupportedInventorApiMajorVersion);
    }

    [Theory]
    [InlineData(31, true)]
    [InlineData(30, false)]
    [InlineData(32, false)]
    public void CompatibilityBoundaryAcceptsOnlyTheVerifiedApi(int majorVersion, bool expected)
    {
        Assert.Equal(expected, ProductIdentity.SupportsInventorApi(majorVersion));
    }
}
