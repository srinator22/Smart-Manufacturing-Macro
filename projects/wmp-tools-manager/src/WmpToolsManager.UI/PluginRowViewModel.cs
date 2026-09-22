// Purpose: Present one plugin row of the update dialog's table.
// Inputs: A PluginSummary, taken from the staged catalog.json where one has been staged and from
//   installed.json before that.
// Outputs: Read-only display strings. The row never changes after construction, so it raises no
//   property-change notifications.
// Dependencies: WmpToolsManager.Application contracts only.
// Assumptions: Maturity is shown in full ("beta - not yet validated in live Inventor") rather than as
//   a bare word, because the table is the one place a user decides whether to trust a plugin.
// Validation source: UpdateWindowRenderTests, which asserts these strings appear in the rendered tree.

using WmpToolsManager.Application;

namespace WmpToolsManager.UI;

public sealed class PluginRowViewModel
{
    public PluginRowViewModel(PluginSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        Id = summary.Id;
        DisplayName = summary.DisplayName;
        Description = summary.Description;
        Maturity = summary.MaturityDescription;
    }

    public string Id { get; }

    public string DisplayName { get; }

    public string Description { get; }

    public string Maturity { get; }
}
