namespace ImportPortal;

public sealed record StepLogEntry(TimeSpan Elapsed, int ThreadId, bool IsThreadPoolThread, string Message);