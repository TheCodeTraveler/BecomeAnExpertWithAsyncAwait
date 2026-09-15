namespace ProductDetails;

// ExceptionType and Exception are set for Crashed. Exception is the full exception, stack trace included, which the workshop guide shows.
// CompletedAt is when the run that produced this state ended, so the workshop guide can show that a click really ran again.
public sealed record StepState(
	StepStatus Status,
	StepReport? Report,
	string? ExceptionType = null,
	string? Exception = null,
	DateTimeOffset? CompletedAt = null);