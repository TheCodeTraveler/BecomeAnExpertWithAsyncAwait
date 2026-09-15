using System.Diagnostics;

namespace OrderPortal;

// Collects what a step logged and every expected result it checked.
// Steps write to it from thread pool threads while the workshop guide reads it, so every member takes the lock.
public sealed class StepReport
{
	readonly Lock _lock = new();
	readonly Stopwatch _stopwatch = Stopwatch.StartNew();
	readonly List<StepLogEntry> _entries = [];
	readonly List<StepCheck> _checks = [];

	bool _isComplete;

	public IReadOnlyList<StepLogEntry> Entries
	{
		get
		{
			lock (_lock)
			{
				return [.. _entries];
			}
		}
	}

	public IReadOnlyList<StepCheck> Checks
	{
		get
		{
			lock (_lock)
			{
				return [.. _checks];
			}
		}
	}

	public void Log(string message)
	{
		var threadId = Environment.CurrentManagedThreadId;
		var isThreadPoolThread = Thread.CurrentThread.IsThreadPoolThread;

		lock (_lock)
		{
			if (!_isComplete)
				_entries.Add(new StepLogEntry(_stopwatch.Elapsed, threadId, isThreadPoolThread, message));
		}
	}

	// The hint is shown under the expected result only when it does not match
	public void Expect<T>(string description, T expected, T actual, string hint)
	{
		Expect(description, Format(expected), Format(actual), EqualityComparer<T>.Default.Equals(expected, actual), hint);
	}

	public void Expect(string description, string expected, string actual, bool passed, string hint)
	{
		lock (_lock)
		{
			if (!_isComplete)
				_checks.Add(new StepCheck(description, expected, actual, passed, hint));
		}
	}

	// Called when the run ends, including a run that timed out or was stopped while its step is still blocked somewhere.
	// Anything that abandoned step writes later is ignored, so the result in the workshop guide can no longer change.
	public void Complete()
	{
		lock (_lock)
		{
			_isComplete = true;
			_stopwatch.Stop();
		}
	}

	static string Format<T>(T value) => value switch
	{
		null => "null",
		int or long => $"{value:N0}",
		_ => value.ToString() ?? "null",
	};
}