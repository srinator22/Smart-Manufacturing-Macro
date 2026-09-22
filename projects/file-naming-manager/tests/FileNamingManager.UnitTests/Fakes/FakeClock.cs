// Purpose: A fixed-time IClock test double so originals-archive timestamps are deterministic.
// Inputs: A UtcNow value set by the test.
// Outputs: That fixed value.
// Dependencies: FileNamingManager.Application.
// Assumptions: None.
// Validation source: n/a - test infrastructure.

using FileNamingManager.Application;

namespace FileNamingManager.UnitTests.Fakes;

public sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 9, 23, 0, 0, 0, TimeSpan.Zero);
}
