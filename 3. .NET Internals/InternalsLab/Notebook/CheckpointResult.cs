namespace InternalsLab;

// Values holds one ObservedValue per prediction column. IsThreadPoolThread is null when the experiment did not record it.
// Details are extra lines shown next to the result, such as the culture and principal Step 2 saw.
public sealed record CheckpointResult(
	int Checkpoint,
	int ThreadId,
	bool? IsThreadPoolThread,
	IReadOnlyDictionary<string, ObservedValue> Values,
	IReadOnlyList<string> Details);