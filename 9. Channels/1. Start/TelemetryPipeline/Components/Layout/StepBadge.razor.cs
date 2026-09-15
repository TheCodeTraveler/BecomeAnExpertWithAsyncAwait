using Microsoft.AspNetCore.Components;

namespace TelemetryPipeline.Components.Layout;

public partial class StepBadgeBase : ComponentBase
{
	[Parameter, EditorRequired]
	public StepStatus Status { get; set; }

	[Parameter]
	public bool IsUnlocked { get; set; } = true;

	// Only the icon, for the collapsed guide
	[Parameter]
	public bool IsCompact { get; set; }

	public string Icon => Status is StepStatus.Running ? "⏳" : !IsUnlocked ? "🔒" : Status switch
	{
		StepStatus.Passed => "✅",
		StepStatus.Failed or StepStatus.TimedOut or StepStatus.Crashed => "❌",
		StepStatus.NotImplemented => "🧩",
		_ => "•",
	};

	public string Label => Status is StepStatus.Running ? "Running" : !IsUnlocked ? "Locked" : Status switch
	{
		StepStatus.Passed => "Passed",
		StepStatus.Failed => "Failed",
		StepStatus.TimedOut => "Timed out",
		StepStatus.Crashed => "Crashed",
		StepStatus.NotImplemented => "Not implemented",
		StepStatus.Stopped => "Stopped",
		_ => "Not run",
	};

	public string CssClass => Status is StepStatus.Running ? "badge-running" : !IsUnlocked ? "badge-locked" : Status switch
	{
		StepStatus.Passed => "badge-passed",
		StepStatus.Failed or StepStatus.TimedOut or StepStatus.Crashed => "badge-failed",
		StepStatus.NotImplemented => "badge-missing",
		_ => "badge-idle",
	};
}