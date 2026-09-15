using Microsoft.AspNetCore.Components;

namespace InternalsLab.Components.Pages;

public partial class HomePageBase : ComponentBase
{
	[Inject]
	public required LabNotebook Notebook { get; init; }

	public int PassedStepCount => Notebook.Steps.Count(step => Notebook.GetStatus(step.Number) is StepStatus.Passed);

	// The first step that has not passed yet, which is always unlocked, or null when every step passed
	public WorkshopStep? NextStep => Notebook.Steps.FirstOrDefault(step => Notebook.GetStatus(step.Number) is not StepStatus.Passed);
}