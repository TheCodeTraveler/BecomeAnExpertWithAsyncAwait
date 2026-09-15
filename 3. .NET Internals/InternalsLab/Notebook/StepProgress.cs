namespace InternalsLab;

// Everything the lab notebook keeps for one step. Predictions and Answers are keyed by WorkshopStep.PredictionKey(...) and ExplainQuestion.Id.
public sealed record StepProgress(
	IReadOnlyDictionary<string, string> Predictions,
	ExperimentRun? LatestRun,
	ExperimentRun? PreviousRun,
	IReadOnlyDictionary<string, string> Answers,
	TryItOutcome? TryItOutcome)
{
	public static StepProgress Empty { get; } = new StepProgress(new Dictionary<string, string>(), null, null, new Dictionary<string, string>(), null);
}