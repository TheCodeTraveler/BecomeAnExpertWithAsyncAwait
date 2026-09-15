namespace CoffeeShop;

// MissingMember is set for NotImplemented, ExceptionType and Exception for Crashed, and WasWaitingForBarista for a TimedOut run
// that ended while a shot was still brewing. Exception is the full exception, stack trace included, which the step page shows.
// CompletedAt is when the run that produced this state ended, so the pages can show that a click really ran again.
public sealed record StepState(
	StepStatus Status,
	StepReport? Report,
	string? MissingMember = null,
	string? ExceptionType = null,
	string? Exception = null,
	bool WasWaitingForBarista = false,
	DateTimeOffset? CompletedAt = null);