namespace CoffeeShop;

// MissingMember is set for NotImplemented, ExceptionType for Crashed, and WasWaitingForBarista for a TimedOut run
// that ended while a shot was still brewing. Only names are kept: exception messages and stack traces stay in the terminal.
// CompletedAt is when the run that produced this state ended, so the pages can show that a click really ran again.
public sealed record StepState(
	StepStatus Status,
	StepReport? Report,
	string? MissingMember = null,
	string? ExceptionType = null,
	bool WasWaitingForBarista = false,
	DateTimeOffset? CompletedAt = null);