namespace TelemetryPipeline;

public record TelemetryEvent(int EventId, string DeviceId, double Reading, DateTimeOffset ReceivedAt);

public record PipelineStats(
	int Accepted,
	int Processed,
	int Dropped,
	int QueueDepth,
	double AverageAcceptMilliseconds,
	double SlowestAcceptMilliseconds,
	double TotalSeconds);