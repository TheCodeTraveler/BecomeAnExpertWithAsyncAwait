namespace InternalsLab;

// One thing to predict at every checkpoint. Most steps predict one value per checkpoint. Step 3 predicts four.
public sealed record PredictionColumn(string Id, string Header, IReadOnlyList<PredictionChoice> Choices);