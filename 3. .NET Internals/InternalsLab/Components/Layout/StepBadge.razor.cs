using Microsoft.AspNetCore.Components;

namespace InternalsLab.Components.Layout;

public partial class StepBadgeBase : ComponentBase
{
	[Parameter, EditorRequired]
	public StepStatus Status { get; set; }

	[Parameter]
	public bool IsUnlocked { get; set; } = true;

	public string Icon => !IsUnlocked ? "🔒" : Status switch
	{
		StepStatus.Predicted => "📝",
		StepStatus.Ran => "🧪",
		StepStatus.Passed => "✅",
		_ => "•",
	};

	public string Label => !IsUnlocked ? "Locked" : Status switch
	{
		StepStatus.Predicted => "Predicted",
		StepStatus.Ran => "Ran",
		StepStatus.Passed => "Passed",
		_ => "Not started",
	};

	public string CssClass => !IsUnlocked ? "badge-locked" : Status switch
	{
		StepStatus.Predicted => "badge-predicted",
		StepStatus.Ran => "badge-ran",
		StepStatus.Passed => "badge-passed",
		_ => "badge-idle",
	};
}