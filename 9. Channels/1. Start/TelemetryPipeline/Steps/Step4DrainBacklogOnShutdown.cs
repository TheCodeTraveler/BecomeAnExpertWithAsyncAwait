using System.Diagnostics;
using System.Threading.Channels;

namespace TelemetryPipeline;

public sealed class Step4DrainBacklogOnShutdown : WorkshopStep
{
	const int _backlogSize = 40;

	const string _abandonedHint = "StopAsync returned before the backlog reached the store. The host cancels stoppingToken at shutdown, and cancelling the read loop abandons every reading still waiting in the queue. "
		+ "Stop passing stoppingToken into DrainAsync, and complete the queue in StopAsync instead, so the consumers finish the backlog and then stop on their own.";

	const string _neverCompletedHint = "StopAsync waited until the shutdown timeout ran out, because nothing told the consumers that no more readings are coming, so they kept waiting for the next one. "
		+ "Override StopAsync in TelemetryProcessor, and complete the queue before you call the base implementation.";

	const string _stillOpenHint = "A reading that arrives during shutdown was queued where no consumer will ever read it, and the device was told everything is fine. "
		+ "StopAsync has to complete the queue, so a late reading is refused with ChannelClosedException instead.";

	// Stands in for the host's shutdown timeout, so a pipeline that never finishes still lets this step end
	static readonly TimeSpan _shutdownTimeout = TimeSpan.FromSeconds(5);
	static readonly TimeSpan _writeTimeout = TimeSpan.FromSeconds(2);

	public override int Number => 4;

	public override string Scenario => "Drain the backlog on shutdown";

	public override string Title => "CompleteWriting() and TelemetryProcessor";

	public override string Story => "A deployment restarts the app while a burst is still waiting in the queue. Every one of those readings was accepted, and every device was told it was safe. "
		+ "Nothing ever tells DrainAsync that the producer is finished: the only thing that stops it is cancellation at shutdown, and cancelling the read loop throws the whole backlog away. "
		+ "The readings never reach the store, and nothing logs a thing.";

	public override string SeeItInTheApp => "Your IDE's Stop button usually ends the process at once and skips the shutdown this step is about, so once Step 3 passes, stop the app in your IDE and start it with dotnet run in the TelemetryPipeline project folder instead. "
		+ "Click Receive 400 events on the Ingest page, and press Ctrl+C in that terminal straight away, while readings are still waiting in the queue. "
		+ "Today the app quits instantly and the backlog is gone. Once this step passes, the app pauses for roughly two seconds before it exits, because it is finishing the backlog first.";

	public override string FileToChange => "Services/TelemetryIngestService.cs, Services/TelemetryProcessor.cs";

	public override IReadOnlyList<string> Tasks { get; } =
	[
		"Implement CompleteWriting() in TelemetryIngestService so it marks the queue complete. Its stub throws NotImplementedException until you do.",
		"Do not call CompleteWriting() from RunBurstAsync. Completing the queue closes it for good, and the next burst would throw ChannelClosedException.",
		"In TelemetryProcessor, override StopAsync and complete the queue before you call the base implementation.",
		"Stop passing stoppingToken into the read loop. Cancelling that loop is exactly what abandons the queued readings.",
	];

	public override IReadOnlyList<string> Clues { get; } =
	[
		"Nothing ever tells DrainAsync that the producer is finished. The only thing that stops it today is cancellation at shutdown.",
		"Once the writer is complete, the reader hands out what is still buffered and then ends on its own.",
		"BackgroundService.StopAsync cancels stoppingToken and then waits for ExecuteAsync to return, for as long as the host's shutdown timeout allows.",
		"More than one code path could reasonably close a channel. The writer has a completion method that returns false instead of throwing when the channel is already complete.",
	];

	public override string TimeoutHint => "Shutting down TelemetryProcessor never finished. Check that StopAsync completes the queue before it waits for the read loop.";

	public override async Task Run(StepContext context)
	{
		var report = context.Report;
		var token = context.Token;

		// Part 1: CompleteWriting() on its own, on a fresh service. A stub that still throws NotImplementedException ends the step here.
		var closedIngest = new TelemetryIngestService(new EventStore());

		report.Log("Calling CompleteWriting() on a fresh service, then posting a reading");
		closedIngest.CompleteWriting();

		var lateReading = await PostLateReading(closedIngest, token).ConfigureAwait(false);

		report.Expect(
			"After CompleteWriting(), AcceptAsync refuses a reading with ChannelClosedException",
			nameof(ChannelClosedException),
			lateReading,
			"CompleteWriting() has to mark the queue complete, so it refuses every later reading and the consumers stop once the backlog is written.");

		// Part 2: the app starts TelemetryProcessor, a burst is queued, and the app shuts down straight away
		var ingest = new TelemetryIngestService(new EventStore());
		using var processor = new TelemetryProcessor(ingest);

		await processor.StartAsync(token).ConfigureAwait(false);
		await ingest.RunBurstAsync(_backlogSize, token).ConfigureAwait(false);

		report.Log($"TelemetryProcessor is running. Stopping it straight after a burst of {_backlogSize}, with {ingest.QueueDepth} readings still waiting and {ingest.Processed} written");

		using var shutdownCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(token);
		shutdownCancellationTokenSource.CancelAfter(_shutdownTimeout);

		var stopwatch = Stopwatch.StartNew();

		try
		{
			await processor.StopAsync(shutdownCancellationTokenSource.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (shutdownCancellationTokenSource.IsCancellationRequested && !token.IsCancellationRequested)
		{
			// The shutdown timeout ran out, which the checks below report
		}

		stopwatch.Stop();

		var processedAtStop = ingest.Processed;
		var readLoopFinished = processor.ExecuteTask?.IsCompleted is true;

		report.Log($"StopAsync returned after {stopwatch.Elapsed.TotalSeconds:F2}s with {processedAtStop} of {_backlogSize} readings written. The read loop {(readLoopFinished ? "has finished" : "is still running")}");

		report.Expect(
			$"StopAsync returns once the read loop finishes, within the {_shutdownTimeout.TotalSeconds} second shutdown timeout",
			"True",
			readLoopFinished.ToString(),
			readLoopFinished,
			_neverCompletedHint);

		report.Expect(
			"Every reading still waiting at shutdown is written before StopAsync returns",
			_backlogSize,
			processedAtStop,
			readLoopFinished ? _abandonedHint : _neverCompletedHint);

		// Part 3: a device posting during shutdown is refused, because the app can no longer promise to store its reading
		var readingDuringShutdown = await PostLateReading(ingest, token).ConfigureAwait(false);

		report.Log($"A reading posted after StopAsync: {readingDuringShutdown}");
		report.Expect("After StopAsync, AcceptAsync refuses a reading with ChannelClosedException", nameof(ChannelClosedException), readingDuringShutdown, _stillOpenHint);
	}

	// Posts one reading and describes how it ended
	static async Task<string> PostLateReading(TelemetryIngestService ingest, CancellationToken token)
	{
		try
		{
			await PostReading(ingest, 0, token).WaitAsync(_writeTimeout, token).ConfigureAwait(false);
			return "accepted";
		}
		catch (ChannelClosedException)
		{
			return nameof(ChannelClosedException);
		}
		catch (TimeoutException)
		{
			return $"still waiting after {_writeTimeout.TotalSeconds} seconds";
		}
	}
}