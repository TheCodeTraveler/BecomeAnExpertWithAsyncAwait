namespace TelemetryPipeline;

public sealed class Step1AnswerRightAway : WorkshopStep
{
	const int _readingCount = 20;
	const double _maximumAverageAcceptMilliseconds = 5;

	const string _waitsForStoreHint = "Every accept waits for the 40 millisecond database write before it answers, so the device holds the connection open for the whole write and gives up after 20. "
		+ "Accepting a reading should only queue it. Move the EventStore.SaveAsync call out of the accept path entirely.";

	const string _countedAsWrittenHint = "AcceptAsync counts the reading as processed, so Written to store counts the same work as Accepted. "
		+ "Only a consumer writes readings to the store, so only a consumer should count them as processed. Nothing is consuming in this check.";

	public override int Number => 1;

	public override string Scenario => "Answer the device right away";

	public override string Title => "AcceptAsync()";

	public override string Story => "Every device in the fleet posts its readings to this endpoint, and every reading has to reach the database. The database takes 40 milliseconds per write. "
		+ "The devices do not care how long that write takes. They care how long the endpoint takes to answer, and they give up after 20 milliseconds. "
		+ "AcceptAsync does the write before it answers, so every reading becomes a retry, and the retries land on an endpoint that is already slow. "
		+ "There is already a background service that was supposed to do this work, and today it does nothing useful.";

	public override string SeeItInTheApp => "On the Ingest page, press Receive 400 events. The button spins for about 16 seconds. "
		+ "Average accept reads about 41ms on a red card, more than twice the 20ms a device waits. "
		+ "Written to store already reads 400 with nothing waiting in the queue, because every write happened inside the accept call. The background service did none of it.";

	public override string FileToChange => "Services/TelemetryIngestService.cs";

	public override IReadOnlyList<string> Tasks { get; } =
	[
		"Change AcceptAsync so it only queues the reading. Move the EventStore.SaveAsync call out of the accept path entirely.",
		"Stop counting the reading as processed in AcceptAsync. Written to store should count readings that actually reached the store, and a consumer does that work.",
		"Return the task type that allocates nothing when the call completes synchronously, because queuing a reading almost always does.",
		"Keep adding the reading to _pending for now. Step 2 replaces the list with a real queue.",
		"Leave EventStore alone. The 40 millisecond write is the one thing you cannot make faster.",
	];

	public override IReadOnlyList<string> Clues { get; } =
	[
		"AcceptAsync awaits eventStore.SaveAsync(...) before it returns. The device holds the connection open for the entire database write.",
		"_processed++ happens in the accept path, so Written to store is counting the same work as Accepted. The consumer is decoration.",
		"Task allocates on every call. Its value type sibling in System.Threading.Tasks does not, when the method completes synchronously.",
	];

	public override string TimeoutHint => "Accepting 20 readings never finished. Check that AcceptAsync no longer waits on anything that could take a long time.";

	public override async Task Run(StepContext context)
	{
		var report = context.Report;
		var token = context.Token;

		// Part 1: warm up on a throwaway service, so compiling AcceptAsync for the first time is not counted as accept time
		await new TelemetryIngestService(new EventStore()).RunBurstAsync(1, token).ConfigureAwait(false);

		// Part 2: the same burst the Ingest page sends, only smaller, to a fresh service with no consumers running
		var ingest = new TelemetryIngestService(new EventStore());
		report.Log($"Receiving a burst of {_readingCount} readings through RunBurstAsync, with no consumers running");

		var stats = await ingest.RunBurstAsync(_readingCount, token).ConfigureAwait(false);
		report.Log($"The burst took {stats.TotalSeconds:F2}s. Average accept {stats.AverageAcceptMilliseconds:F1}ms, slowest {stats.SlowestAcceptMilliseconds:F1}ms");

		report.Expect(
			$"Accepting a reading takes under {_maximumAverageAcceptMilliseconds}ms on average",
			$"under {_maximumAverageAcceptMilliseconds}ms",
			$"{stats.AverageAcceptMilliseconds:F1}ms",
			stats.AverageAcceptMilliseconds < _maximumAverageAcceptMilliseconds,
			_waitsForStoreHint);

		report.Expect($"Accepted reads {_readingCount}", _readingCount, ingest.Accepted, "Every reading AcceptAsync takes has to be counted in Accepted, once.");

		// Part 3: nothing is draining this service, so a reading that was only queued has not been written yet
		report.Log($"Right after the burst, Processed reads {ingest.Processed} and QueueDepth reads {ingest.QueueDepth}");

		report.Expect("Right after the burst, nothing has been written to the store yet", 0, ingest.Processed, _countedAsWrittenHint);
		report.Expect($"Right after the burst, all {_readingCount} readings are waiting in the queue", _readingCount, ingest.QueueDepth, "Accepting a reading has to put it in the queue, where a consumer can pick it up later.");

		// Part 4: the return type. A ValueTask that completes synchronously allocates nothing, and this call is made constantly.
		var acceptAsync = typeof(TelemetryIngestService).GetMethod(nameof(TelemetryIngestService.AcceptAsync), [typeof(TelemetryEvent), typeof(CancellationToken)]);

		report.Expect(
			"AcceptAsync returns ValueTask",
			nameof(ValueTask),
			acceptAsync?.ReturnType.Name ?? "no AcceptAsync(TelemetryEvent, CancellationToken) method",
			acceptAsync?.ReturnType == typeof(ValueTask),
			"Queuing a reading almost always completes synchronously, and a fleet of devices calls this constantly. Return the task type that allocates nothing when the call completes synchronously.");
	}
}