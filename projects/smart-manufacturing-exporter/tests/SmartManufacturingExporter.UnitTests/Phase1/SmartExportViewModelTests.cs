using SmartManufacturingExporter.Application.Phase1;
using SmartManufacturingExporter.Core.Phase1;
using SmartManufacturingExporter.UI.Phase1;

namespace SmartManufacturingExporter.UnitTests.Phase1;

public sealed class SmartExportViewModelTests
{
    private const string Destination = @"C:\Exports";

    [Fact]
    public void ConstructorSelectsEveryCandidateByDefault()
    {
        Fixture fixture = CreateFixture();

        Assert.All(fixture.ViewModel.Rows, row => Assert.True(row.IsSelected));
        Assert.True(fixture.ViewModel.CanExport);
        Assert.Equal(@"C:\Models\Machine.iam", fixture.ViewModel.RootAssemblyPath);
    }

    [Fact]
    public void ConstructorExposesStablePrecisionOptionsAndDefaultsToLow()
    {
        Fixture fixture = CreateFixture();

        Assert.Same(fixture.ViewModel.StepPrecisionOptions, fixture.ViewModel.StepPrecisionOptions);
        Assert.Equal(
            [StepExportPrecision.Low, StepExportPrecision.Medium, StepExportPrecision.Highest],
            fixture.ViewModel.StepPrecisionOptions);
        Assert.Equal(StepExportPrecision.Low, fixture.ViewModel.SelectedStepPrecision);
        Assert.Contains("spline-fit", fixture.ViewModel.StepPrecisionDescription, StringComparison.Ordinal);
        Assert.Contains("file size", fixture.ViewModel.StepPrecisionDescription, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SelectingPrecisionUpdatesDescriptionAndRaisesNotifications()
    {
        Fixture fixture = CreateFixture();
        List<string?> changedProperties = [];
        fixture.ViewModel.PropertyChanged += (_, eventArgs) => changedProperties.Add(eventArgs.PropertyName);

        fixture.ViewModel.SelectedStepPrecision = StepExportPrecision.Highest;

        Assert.Contains(nameof(SmartExportViewModel.SelectedStepPrecision), changedProperties);
        Assert.Contains(nameof(SmartExportViewModel.StepPrecisionDescription), changedProperties);
        Assert.Contains("finest", fixture.ViewModel.StepPrecisionDescription, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SelectNoneAndSelectAllUpdateRowsAndCanExport()
    {
        Fixture fixture = CreateFixture();

        fixture.ViewModel.SelectNone();

        Assert.All(fixture.ViewModel.Rows, row => Assert.False(row.IsSelected));
        Assert.False(fixture.ViewModel.CanExport);

        fixture.ViewModel.SelectAll();

        Assert.All(fixture.ViewModel.Rows, row => Assert.True(row.IsSelected));
        Assert.True(fixture.ViewModel.CanExport);
    }

    [Fact]
    public void ExportSelectedExportsOnlyCheckedRows()
    {
        Fixture fixture = CreateFixture();
        fixture.ViewModel.Rows[0].IsSelected = false;

        fixture.ViewModel.ExportSelected();

        Assert.Equal(
            [(@"C:\Models\Beta.ipt", @"C:\Exports\Beta.step", StepExportPrecision.Low)],
            fixture.Gateway.ExportCalls);
        Assert.Equal("Export complete: 1 succeeded, 0 failed.", fixture.ViewModel.StatusMessage);
    }

    [Fact]
    public void ExportSelectedPassesHighestPrecisionToEveryCheckedExport()
    {
        Fixture fixture = CreateFixture();
        fixture.ViewModel.Rows[0].IsSelected = false;
        fixture.ViewModel.SelectedStepPrecision = StepExportPrecision.Highest;

        fixture.ViewModel.ExportSelected();

        Assert.Equal(
            [(@"C:\Models\Beta.ipt", @"C:\Exports\Beta.step", StepExportPrecision.Highest)],
            fixture.Gateway.ExportCalls);
    }

    [Fact]
    public void InvalidDestinationReportsValidationAndDoesNotExport()
    {
        Fixture fixture = CreateFixture(directoryExists: false);

        fixture.ViewModel.ExportSelected();

        Assert.Empty(fixture.Gateway.ExportCalls);
        Assert.Contains("does not exist", fixture.ViewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExportSelectedSummarizesPerItemFailure()
    {
        Fixture fixture = CreateFixture();
        fixture.Gateway.Failures[@"C:\Models\Alpha.ipt"] = new InvalidOperationException("Translator failed.");

        fixture.ViewModel.ExportSelected();

        Assert.Equal("Export complete: 1 succeeded, 1 failed. Alpha.ipt: Translator failed.", fixture.ViewModel.StatusMessage);
    }

    private static Fixture CreateFixture(bool directoryExists = true)
    {
        FakeGateway gateway = new();
        FakeFileSystem fileSystem = new() { DirectoryExistsResult = directoryExists };
        SmartExportWorkflow workflow = new(gateway, fileSystem);
        Phase1StartResult session = workflow.Start();
        SmartExportViewModel viewModel = new(workflow, session) { DestinationDirectory = Destination };
        return new(viewModel, gateway);
    }

    private sealed record Fixture(SmartExportViewModel ViewModel, FakeGateway Gateway);

    private sealed class FakeGateway : IInventorPhase1Gateway
    {
        public List<(string SourcePath, string OutputPath, StepExportPrecision Precision)> ExportCalls { get; } = [];

        public Dictionary<string, Exception> Failures { get; } = new(StringComparer.OrdinalIgnoreCase);

        public ActiveAssemblyScan? ScanActiveAssembly() => new(
            @"C:\Models\Machine.iam",
            [
                new("Alpha:1", @"C:\Models\Alpha.ipt", ComponentDocumentKind.Part, false),
                new("Beta:1", @"C:\Models\Beta.ipt", ComponentDocumentKind.Part, false),
            ]);

        public void ExportPartAsStep(
            string sourcePath,
            string outputPath,
            StepExportPrecision precision)
        {
            ExportCalls.Add((sourcePath, outputPath, precision));
            if (Failures.TryGetValue(sourcePath, out Exception? failure))
            {
                throw failure;
            }
        }
    }

    private sealed class FakeFileSystem : IPhase1FileSystem
    {
        public bool DirectoryExistsResult { get; init; } = true;

        public bool DirectoryExists(string path) => DirectoryExistsResult;

        public bool CanWriteToDirectory(string path) => true;

        public bool FileExists(string path) => false;
    }
}
