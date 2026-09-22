// Purpose: Distinguish a preview-only naming session from one allowed to execute a rename plan.
// Inputs: n/a (enum).
// Outputs: The mode a FileNamingViewModel/FileNamingWindow was constructed with.
// Dependencies: none.
// Assumptions: Mode is fixed for the lifetime of a window; switching modes means opening a new one.
// Validation source: .work/TASK.md UI acceptance criterion (window shows mode Analyze or Apply).

namespace FileNamingManager.UI;

public enum FileNamingMode
{
    Analyze,
    Apply,
}
