namespace OrderPortal;

// Every step is one bug in OrderPortal's shared services.
// Its tab in the workshop guide tells you what to fix, and Run() drives your code the way a sale does, logs what happens,
// and records an expected result, with a hint, for everything that should be true once the bug is fixed.
public abstract class WorkshopStep
{
	public abstract int Number { get; }

	// What this step fixes, in a few words
	public abstract string Scenario { get; }

	// The members this step exercises
	public abstract string Title { get; }

	// What goes wrong in production, and why nobody noticed with one customer at a time
	public abstract string Story { get; }

	// How to watch this bug happen on the Checkout page
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
}