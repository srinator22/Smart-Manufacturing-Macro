using SmartManufacturingExporter.Infrastructure.Phase1;

namespace SmartManufacturingExporter.UnitTests.Phase1;

public sealed class PhysicalPhase1FileSystemTests
{
    [Fact]
    public void CanWriteToDirectoryReturnsFalseForMissingDirectory()
    {
        PhysicalPhase1FileSystem fileSystem = new();
        string missingDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        Assert.False(fileSystem.CanWriteToDirectory(missingDirectory));
    }

    [Fact]
    public void CanWriteToDirectoryAllowsAddingFilesToOwnedTemporaryDirectoryOnWindows()
    {
        PhysicalPhase1FileSystem fileSystem = new();
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        if (!OperatingSystem.IsWindows())
        {
            Assert.False(fileSystem.CanWriteToDirectory(Path.GetTempPath()));
            return;
        }

        Directory.CreateDirectory(directory);
        try
        {
            Assert.True(fileSystem.CanWriteToDirectory(directory));
        }
        finally
        {
            Directory.Delete(directory);
        }
    }
}
