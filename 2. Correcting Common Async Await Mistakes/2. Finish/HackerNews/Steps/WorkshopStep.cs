namespace HackerNews;

// Every step is one async await mistake in the Top stories page.
// Its tab in the workshop guide tells you what to fix, and Run() renders your News page the way Blazor does for a browser tab, logs what happens,
// and records an expected result, with a hint, for everything that should be true once the mistake is fixed.
public abstract class WorkshopStep
{
	public abstract int Number { get; }

	// What this step fixes, in a few words
	public abstract string Scenario { get; }

	// The members this step exercises
	public abstract string Title { get; }

	// What goes wrong in production, and why it slipped through
	public abstract string Story { get; }

	// How to watch this mistake happen on the Top stories page
	public abstract string SeeItInTheApp { get; }

	// Where the mistake lives
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