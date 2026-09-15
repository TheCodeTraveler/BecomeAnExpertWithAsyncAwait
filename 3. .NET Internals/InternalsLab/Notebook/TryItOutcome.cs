namespace InternalsLab;

// What happened when the Try it button ran the version of Step 2 that awaits inside the using block.
// Exception is the full exception, with its stack trace, and the Step 2 page shows it: seeing it is the point of Try it.
public sealed record TryItOutcome(
	string? ExceptionType,
	string? Exception,
	int? UsingBlockStartThreadId,
	int? UsingBlockEndThreadId,
	DateTimeOffset RanAt);