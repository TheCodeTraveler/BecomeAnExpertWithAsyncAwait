namespace TelemetryPipeline;

public enum StepStatus
{
	NotRun,
	Running,
	Passed,
	Failed,
	NotImplemented,
	TimedOut,
	Crashed,
	Stopped,
}