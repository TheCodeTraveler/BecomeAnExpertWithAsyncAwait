namespace InternalsLab;

// Locked is not a status: a step is locked until the step before it passes, whatever it holds
public enum StepStatus
{
	NotStarted,
	Predicted,
	Ran,
	Passed,
}