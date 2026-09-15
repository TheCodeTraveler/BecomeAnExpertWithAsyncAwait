namespace InternalsLab;

// Workshop plumbing: records what the experiment code sees at each checkpoint, together with the thread that saw it.
// Several threads record at the same time, so every member takes the lock.
public sealed class CheckpointLog<T>
{
	readonly Lock _lock = new();
	readonly List<Observation<T>> _observations = [];

	public IReadOnlyList<Observation<T>> Observations
	{
		get
		{
			lock (_lock)
			{
				return [.. _observations.OrderBy(static observation => observation.Checkpoint)];
			}
		}
	}

	public void Record(int checkpoint, T value)
	{
		var observation = new Observation<T>(checkpoint, Environment.CurrentManagedThreadId, Thread.CurrentThread.IsThreadPoolThread, value);

		lock (_lock)
		{
			_observations.Add(observation);
		}
	}
}