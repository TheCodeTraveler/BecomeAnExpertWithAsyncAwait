using System.Diagnostics;

namespace ImportPortal;

// Every step is one stage of ImportPortal's nightly import.
// Its tab in the workshop guide tells you what to fix, and Run() runs your ImportService on fresh services, logs what it measured,
// and records an expected result, with a hint, for everything that should be true once the stage is fixed.
public abstract class WorkshopStep
{
	public abstract int Number { get; }

	// What this step fixes, in a few words
	public abstract string Scenario { get; }

	// The members this step exercises
	public abstract string Title { get; }

	// What goes wrong in production, and why nobody noticed
	public abstract string Story { get; }

	// How to watch this bug happen on the Import page
	public abstract string SeeItInTheApp { get; }

	// Where the bug lives
	public abstract string FileToChange { get; }

	// What this step asks you to change. Some of it cannot be measured from outside, so not every task has an expected result.
	public abstract IReadOnlyList<string> Tasks { get; }

	// Shown only when you ask for them
	public abstract IReadOnlyList<string> Clues { get; }

	// What is most likely waiting forever when this step does not finish in time
	public abstract string TimeoutHint { get; }

	public string SourceFile => $"Steps/{GetType().Name}.cs";

	public abstract Task Run(StepContext context);

	// The CPU time every thread in this app has used so far. The difference between two readings, divided by the
	// wall-clock time between them, is how many cores that work kept busy on average.
	protected static TimeSpan GetProcessorTime()
	{
		using var process = Process.GetCurrentProcess();

		return process.TotalProcessorTime;
	}
}