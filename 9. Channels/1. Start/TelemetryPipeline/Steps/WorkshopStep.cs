namespace TelemetryPipeline;

// Every step is one problem in TelemetryPipeline's ingest path.
// Its tab in the workshop guide tells you what to fix, and Run() drives your code the way the device fleet does, logs what happens,
// and records an expected result, with a hint, for everything that should be true once the problem is fixed.
public abstract class WorkshopStep
{
	public abstract int Number { get; }

	// What this step fixes, in a few words
	public abstract string Scenario { get; }

	// The members this step exercises
	public abstract string Title { get; }

	// What goes wrong in production, and why nobody noticed with one device at a time
	public abstract string Story { get; }

	// How to watch this problem happen on the Ingest page
	public abstract string SeeItInTheApp { get; }

	// Where the problem lives
	public abstract string FileToChange { get; }

	// What this step asks you to change. Some of it cannot be measured from outside, so not every task has an expected result.
	public abstract IReadOnlyList<string> Tasks { get; }

	// Shown only when you ask for them
	public abstract IReadOnlyList<string> Clues { get; }

	// What is most likely waiting forever when this step does not finish in time
	public abstract string TimeoutHint { get; }

	public string SourceFile => $"Steps/{GetType().Name}.cs";

	public abstract Task Run(StepContext context);

	// A device posts one reading. AcceptAsync returns Task in the starter code and ValueTask once you change it,
	// and awaiting it inside this async method works for both. The Task this returns is already complete
	// when the reading was queued synchronously, and an exception from AcceptAsync is stored in it rather than thrown.
	protected static async Task PostReading(TelemetryIngestService ingest, int eventId, CancellationToken token)
	{
		var reading = new TelemetryEvent(eventId, $"device-{eventId % 20}", eventId * 0.37, DateTimeOffset.UtcNow);

		await ingest.AcceptAsync(reading, token).ConfigureAwait(false);
	}
}