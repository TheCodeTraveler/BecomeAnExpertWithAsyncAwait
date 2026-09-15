using Microsoft.AspNetCore.Components;

namespace InternalsLab.Components.Layout;

// Renders step text with its links (see StepText): code links open the line on GitHub or in VS Code, and #links scroll to a panel on the step page
public partial class LinkedTextBase : ComponentBase
{
	[Parameter, EditorRequired]
	public required string Text { get; set; }

	[Parameter, EditorRequired]
	public CodeEditor Editor { get; set; }

	[Parameter, EditorRequired]
	public int StepNumber { get; set; }

	// The ids of the panels the page is showing right now. A #link to a panel that is not showing yet is shown as bold text instead.
	[Parameter]
	public IReadOnlyCollection<string> Anchors { get; set; } = [];

	protected static CodeLocation FindLocation(string target)
	{
		var markerIndex = target.IndexOf('#', StringComparison.Ordinal);

		return markerIndex < 0 ? SourceCode.Find(target) : SourceCode.Find(target[..markerIndex], target[(markerIndex + 1)..]);
	}
}
