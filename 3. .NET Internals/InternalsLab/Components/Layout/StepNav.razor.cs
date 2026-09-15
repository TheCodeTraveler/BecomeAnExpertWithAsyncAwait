using Microsoft.AspNetCore.Components;

namespace InternalsLab.Components.Layout;

public partial class StepNavBase : ComponentBase
{
	// A parameter rather than [Inject]: a reference-type parameter makes the nav re-render every time its page does
	[Parameter, EditorRequired]
	public required LabNotebook Notebook { get; set; }
}