namespace InternalsLab;

// What happened when the Try it button ran the version of Step 2 that awaits inside the using block.
// Only the exception type is kept: the message and stack trace stay in the terminal.
public sealed record TryItOutcome(
	string? ExceptionType,
	int? UsingBlockStartThreadId,
	int? UsingBlockEndThreadId,
	DateTimeOffset RanAt);