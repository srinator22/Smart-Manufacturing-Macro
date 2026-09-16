using SmartManufacturingExporter.Core.Phase1;

namespace SmartManufacturingExporter.UnitTests.Phase1;

public sealed class StepExportPrecisionTests
{
    [Theory]
    [InlineData(StepExportPrecision.Low, 0.001)]
    [InlineData(StepExportPrecision.Medium, 0.0001)]
    [InlineData(StepExportPrecision.Highest, 0.00001)]
    public void FitToleranceMatchesDocumentedInventorCentimeterValues(
        StepExportPrecision precision,
        double expectedCentimeters)
    {
        double actualCentimeters = precision.GetFitToleranceCentimeters();

        Assert.Equal(expectedCentimeters, actualCentimeters);
    }

    [Fact]
    public void UnsupportedPrecisionCannotProducePlausibleTolerance()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ((StepExportPrecision)99).GetFitToleranceCentimeters());
    }
}
