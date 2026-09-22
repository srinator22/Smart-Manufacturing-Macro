// Purpose: Provide the real wall-clock time to FileNamingWorkflow's originals-archive timestamp.
// Inputs: None.
// Outputs: The current UTC time.
// Dependencies: System only.
// Assumptions: None.
// Validation source: n/a - trivial system wrapper; testability comes from IClock at the call site.

using FileNamingManager.Application;

namespace FileNamingManager.Infrastructure;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
