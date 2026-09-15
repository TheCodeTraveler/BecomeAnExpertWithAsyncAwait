namespace TelemetryPipeline;

// MissingMember is set for NotImplemented and ExceptionType for Crashed. Only names are kept: exception messages and stack traces stay in the terminal.
// CompletedAt is when the run that produced this state ended, so the workshop guide can show that a click really ran again.
public sealed record StepState(
	StepStatus Status,
	StepReport? Report,
	string? MissingMember = null,
	string? ExceptionType = null,
	DateTimeOffset? CompletedAt = null);