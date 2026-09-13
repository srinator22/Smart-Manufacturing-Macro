namespace SmartManufacturingExporter.Core;

/// <summary>
/// Defines the compatibility boundary shared by every project in the solution.
/// </summary>
public static class ProductIdentity
{
    public const string ProductName = "Smart Manufacturing Exporter";
    public const int SupportedInventorReleaseYear = 2027;
    public const int SupportedInventorApiMajorVersion = 31;

    public static bool SupportsInventorApi(int majorVersion) =>
        majorVersion == SupportedInventorApiMajorVersion;
}
