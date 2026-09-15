namespace TelemetryPipeline;

public sealed record StepCheck(string Description, string Expected, string Actual, bool Passed, string Hint);