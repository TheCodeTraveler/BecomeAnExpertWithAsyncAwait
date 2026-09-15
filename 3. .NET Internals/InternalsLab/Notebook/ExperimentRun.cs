namespace InternalsLab;

// Predictions is a copy of your predictions when the experiment ran, so the results always compare against what you predicted
public sealed record ExperimentRun(
	int RunNumber,
	IReadOnlyList<CheckpointResult> Results,
	IReadOnlyDictionary<string, string> Predictions,
	DateTimeOffset RanAt);