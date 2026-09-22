// The expected strings are pinned verbatim: they are the contract between this add-in and
// scripts/release/Install-WmpInventorTools.ps1, and a silently reordered or renamed switch would be a
// live-only failure that no other test could see.

using WmpToolsManager.Core;

namespace WmpToolsManager.UnitTests;

public sealed class ApplyCommandLineTests
{
    private const string Installer = @"C:\Users\Tester\AppData\Local\WMP\InventorTools\staging\0.6.0\Install-WmpInventorTools.ps1";
    private const string Zip = @"C:\Users\Tester\AppData\Local\WMP\InventorTools\staging\0.6.0\WmpInventorTools-0.6.0.zip";
    private const string Sums = @"C:\Users\Tester\AppData\Local\WMP\InventorTools\staging\0.6.0\SHA256SUMS.txt";
    private const string AddinsRoot = @"C:\Users\Tester\AppData\Roaming\Autodesk\Inventor 2027\Addins";
    private const string StateRoot = @"C:\Users\Tester\AppData\Local\WMP\InventorTools";

    [Fact]
    public void RunsWindowsPowerShell() => Assert.Equal("powershell.exe", ApplyCommandLine.ExecutableFileName);

    [Fact]
    public void NamesTheInstallerScriptTheReleasePublishes() =>
        Assert.Equal("Install-WmpInventorTools.ps1", ApplyCommandLine.InstallerFileName);

    [Fact]
    public void BuildsTheApplyCommandLineTheInstallerExpects()
    {
        string arguments = ApplyCommandLine.BuildApplyArguments(Installer, Zip, Sums, AddinsRoot, StateRoot);

        Assert.Equal(
            $"-NoProfile -ExecutionPolicy Bypass -File \"{Installer}\" -ZipPath \"{Zip}\" " +
            $"-Sha256SumsPath \"{Sums}\" -WaitForInventor -AddinsRoot \"{AddinsRoot}\" -StateRoot \"{StateRoot}\"",
            arguments);
    }

    [Fact]
    public void BuildsTheRollbackCommandLineTheInstallerExpects()
    {
        string arguments = ApplyCommandLine.BuildRollbackArguments(Installer, AddinsRoot, StateRoot);

        Assert.Equal(
            $"-NoProfile -ExecutionPolicy Bypass -File \"{Installer}\" -Rollback -WaitForInventor " +
            $"-AddinsRoot \"{AddinsRoot}\" -StateRoot \"{StateRoot}\"",
            arguments);
    }

    [Fact]
    public void RollbackNeverPassesAPackage()
    {
        string arguments = ApplyCommandLine.BuildRollbackArguments(Installer, AddinsRoot, StateRoot);

        Assert.DoesNotContain("-ZipPath", arguments, StringComparison.Ordinal);
        Assert.DoesNotContain("-Sha256SumsPath", arguments, StringComparison.Ordinal);
    }

    [Fact]
    public void QuotesEveryPathSoASpaceInTheAddinsRootSurvives()
    {
        string arguments = ApplyCommandLine.BuildApplyArguments(Installer, Zip, Sums, AddinsRoot, StateRoot);

        Assert.Contains($"-AddinsRoot \"{AddinsRoot}\"", arguments, StringComparison.Ordinal);
        Assert.Contains("Inventor 2027", arguments, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RefusesAnEmptyPath(string? empty)
    {
        Assert.ThrowsAny<ArgumentException>(
            () => ApplyCommandLine.BuildApplyArguments(empty!, Zip, Sums, AddinsRoot, StateRoot));
        Assert.ThrowsAny<ArgumentException>(
            () => ApplyCommandLine.BuildApplyArguments(Installer, empty!, Sums, AddinsRoot, StateRoot));
        Assert.ThrowsAny<ArgumentException>(
            () => ApplyCommandLine.BuildApplyArguments(Installer, Zip, empty!, AddinsRoot, StateRoot));
        Assert.ThrowsAny<ArgumentException>(
            () => ApplyCommandLine.BuildApplyArguments(Installer, Zip, Sums, empty!, StateRoot));
        Assert.ThrowsAny<ArgumentException>(
            () => ApplyCommandLine.BuildApplyArguments(Installer, Zip, Sums, AddinsRoot, empty!));
        Assert.ThrowsAny<ArgumentException>(
            () => ApplyCommandLine.BuildRollbackArguments(empty!, AddinsRoot, StateRoot));
    }

    [Fact]
    public void RefusesAPathCarryingADoubleQuoteRatherThanEscapingIt()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => ApplyCommandLine.BuildApplyArguments(Installer, "C:\\a\"b.zip", Sums, AddinsRoot, StateRoot));

        Assert.Contains("double quote", exception.Message, StringComparison.Ordinal);
        Assert.Equal("zipPath", exception.ParamName);
    }
}
