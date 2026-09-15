using System.Reflection;
using System.Threading.Channels;
using TelemetryPipeline.Components.Pages;

namespace TelemetryPipeline;

public sealed class Step2QueueIntoBoundedChannel : WorkshopStep
{
	const int _maximumWrites = 10_000;
	const int _deviceCount = 16;

	const BindingFlags _allFields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

	const string _channelHint = "The queue has to be thread safe, have a capacity, and be awaitable, so a full queue can make a device wait and an empty one can make a consumer wait without polling. "
		+ "System.Threading.Channels has exactly that type, and it ships with .NET.";

	const string _listHint = "List<T> is not thread safe, and nothing limits how large it can grow. Replace it with the channel, rather than keeping both.";

	const string _capacityHint = "The queue made a device wait before the whole burst fit. Pick a capacity large enough to hold the whole 400 reading burst, so a burst is absorbed without anyone waiting.";

	const string _unboundedHint = "Nothing limits how large the queue can grow, so a big enough burst is an out of memory exception. Create the queue with a capacity, and make a full queue wait for room.";

	const string _droppedHint = "The queue stayed at its capacity while every write still completed right away, so readings were thrown away with no exception and no log line. "
		+ "Choose the full mode that makes the producer wait instead of losing readings.";

	const string _skippedHint = "No write ever waited for room, so there was no waiting write to check. Make a full queue wait first.";

	const string _lostReadingsHint = "Readings went missing, or a write threw, when many devices posted at the same time. "
		+ "Let the thread safe queue take the readings, update the counter atomically, and do not promise the channel a single writer when many devices write.";

	static readonly TimeSpan _writeTimeout = TimeSpan.FromSeconds(2);

	public override int Number => 2;

	public override string Scenario => "Queue into a bounded channel";

	public override string Title => "The queue, Accepted, Processed and QueueDepth";

	public override string Story => "The endpoint answers right away now, but it is answering into a List. Every browser session shares this one singleton, so many devices add to that list at once while the drain loop removes from it on another thread. "
		+ "List<T> is not thread safe: readings go missing, or the list throws. Nothing limits how large it can grow, so a big enough burst is an out of memory exception. "
		+ "And nothing can wait on it, which is why the drain loop has to poll.";

	public override string SeeItInTheApp => "This one hides from a single click. Once Step 1 passes, press Receive 400 events on the Ingest page and Accepted still reads 400 of 400, because the page posts one reading at a time. "
		+ "The list only breaks when many devices post at the same moment, which is exactly what this step does: 16 of them, at the same instant.";

	public override string FileToChange => "Services/TelemetryIngestService.cs";

	public override IReadOnlyList<string> Tasks { get; } =
	[
		"Add a using for the namespace that holds Channel<T>.",
		"Replace the List<TelemetryEvent> with a queue that is thread safe, has a capacity you choose, and can be awaited. Add a capacity constant large enough to hold the whole 400 reading burst.",
		"Decide what a full queue should do to the device that is writing, and pick the option that makes the producer wait instead of losing readings.",
		"Be honest in the options about how many producers and consumers there really are. Many devices write, and you are about to run more than one consumer.",
		"Change AcceptAsync so it writes the reading to the queue, passes its CancellationToken along, and counts the reading as accepted only once it is queued.",
		"Make the counters safe for a producer and several consumers to touch at once, and make sure the Blazor page reads the latest value rather than one cached in a register.",
		"Report QueueDepth from the queue itself instead of a list count.",
		"DrainAsync, Reset() and RunBurstAsync still use the list, so the project will not build until they stop. Step 3 rewrites DrainAsync and removes Reset(), so a quick change is fine for now.",
	];

	public override IReadOnlyList<string> Clues { get; } =
	[
		"_pending is a List<TelemetryEvent> that the accept path writes while DrainAsync reads it. List<T> is not thread safe, so items can be lost and the list can throw.",
		"Nothing limits how large _pending can grow. A big enough burst is an out of memory exception.",
		"_accepted++ and _processed++ are not atomic, and TelemetryIngestService is a singleton shared by every browser session.",
		"Channel.CreateBounded takes an options object with a capacity, a full mode, and two promises about readers and writers. The promises are not enforced: break one and nothing throws, it corrupts.",
		"Interlocked updates an int atomically, and Volatile reads one so the value is never stale.",
	];

	public override string TimeoutHint => "Writing to the queue never finished. Check that AcceptAsync passes its CancellationToken to the write, so a device that gives up can stop waiting.";

	public override async Task Run(StepContext context)
	{
		var report = context.Report;
		var token = context.Token;

		// Part 1: the queue itself. Reflection lists the fields TelemetryIngestService declares.
		var fields = typeof(TelemetryIngestService).GetFields(_allFields);
		var channelField = fields.FirstOrDefault(static field => typeof(Channel<TelemetryEvent, TelemetryEvent>).IsAssignableFrom(field.FieldType));
		var listField = fields.FirstOrDefault(static field => typeof(List<TelemetryEvent>).IsAssignableFrom(field.FieldType));

		report.Log($"TelemetryIngestService declares these fields: {string.Join(", ", fields.Select(static field => field.Name))}");

		report.Expect("TelemetryIngestService queues readings in a Channel<TelemetryEvent>", "a Channel<TelemetryEvent> field", channelField?.Name ?? "none", channelField is not null, _channelHint);
		report.Expect("No List<TelemetryEvent> is left", "none", listField?.Name ?? "none", _listHint);

		// Part 2: fill a fresh queue until a write waits for room, or until 10,000 readings, 25 times the burst, fit without anyone waiting
		var fullIngest = new TelemetryIngestService(new EventStore());
		using var waitingWriteCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(token);

		report.Log($"Posting readings one at a time until a write waits for room, up to {_maximumWrites:N0}");

		Task? waitingWrite = null;
		var completedWrites = 0;

		while (waitingWrite is null && completedWrites < _maximumWrites)
		{
			var write = PostReading(fullIngest, completedWrites, waitingWriteCancellationTokenSource.Token);

			if (write.IsCompleted)
			{
				// Rethrows anything AcceptAsync threw
				await write.ConfigureAwait(false);
				completedWrites++;
			}
			else
			{
				waitingWrite = write;
			}
		}

		var queueDepth = fullIngest.QueueDepth;

		report.Log(waitingWrite is null
			? $"All {completedWrites:N0} writes completed right away, and QueueDepth reads {queueDepth:N0}"
			: $"{completedWrites:N0} writes completed right away, then write {completedWrites + 1:N0} waited for room. QueueDepth reads {queueDepth:N0}");

		report.Expect(
			$"The queue holds the whole {IngestPageBase.EventCount} reading burst without making a device wait",
			$"{IngestPageBase.EventCount} of {IngestPageBase.EventCount} completed right away",
			$"{Math.Min(completedWrites, IngestPageBase.EventCount)} of {IngestPageBase.EventCount} completed right away",
			completedWrites >= IngestPageBase.EventCount,
			_capacityHint);

		report.Expect(
			"A full queue makes the next device wait for room, instead of growing forever or dropping a reading",
			"a write waits for room",
			waitingWrite is null ? $"{_maximumWrites:N0} writes completed right away, {queueDepth:N0} still queued" : $"write {completedWrites + 1:N0} waited for room",
			waitingWrite is not null,
			queueDepth < completedWrites ? _droppedHint : _unboundedHint);

		if (waitingWrite is null)
		{
			report.Expect("When the queue is full, QueueDepth reads its capacity", "the capacity", "Skipped: no write waited for room", false, _skippedHint);
			report.Expect("A waiting write stops when the device gives up and cancels it", "cancelled", "Skipped: no write waited for room", false, _skippedHint);
		}
		else
		{
			report.Expect("When the queue is full, QueueDepth reads its capacity", completedWrites, queueDepth, "QueueDepth has to come from the queue itself, so it reads how many readings are waiting in it right now.");

			// Part 3: a device that gives up cancels its request. The write waiting for room has to stop, and must not be counted.
			report.Log("The device waiting for room gives up and cancels its write");
			await waitingWriteCancellationTokenSource.CancelAsync().ConfigureAwait(false);

			var cancelledWrite = await Outcome(waitingWrite, token).ConfigureAwait(false);
			report.Log($"The waiting write: {cancelledWrite}");

			report.Expect("A waiting write stops when the device gives up and cancels it", "cancelled", cancelledWrite, "Pass the CancellationToken AcceptAsync receives to the write, so a device that gives up stops waiting for room.");
			report.Expect("The cancelled reading is not counted as accepted", completedWrites, fullIngest.Accepted, "Count a reading as accepted only after the write to the queue has completed.");
		}

		// Part 4: 16 devices wait at a Barrier, then each posts its share of the burst at the same instant
		var sharedIngest = new TelemetryIngestService(new EventStore());
		var readingsPerDevice = IngestPageBase.EventCount / _deviceCount;
		var writes = new Task[_deviceCount * readingsPerDevice];

		using var sharedWriteCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(token);
		using var barrier = new Barrier(_deviceCount);

		report.Log($"{_deviceCount} devices are each posting {readingsPerDevice} readings at the same instant");

		var devices = Enumerable.Range(0, _deviceCount)
			.Select(device => new Thread(() =>
			{
				barrier.SignalAndWait();

				// PostReading never throws. Anything AcceptAsync throws is kept in the Task, so it cannot end the app from this thread.
				for (var reading = 0; reading < readingsPerDevice; reading++)
				{
					var eventId = (device * readingsPerDevice) + reading;
					writes[eventId] = PostReading(sharedIngest, eventId, sharedWriteCancellationTokenSource.Token);
				}
			}))
			.ToList();

		devices.ForEach(static device => device.Start());
		devices.ForEach(static device => device.Join());

		await Task.WhenAny(Task.WhenAll(writes), Task.Delay(_writeTimeout, token)).ConfigureAwait(false);
		await sharedWriteCancellationTokenSource.CancelAsync().ConfigureAwait(false);

		var failedWrites = writes.Where(static write => !write.IsCompletedSuccessfully).ToList();

		foreach (var failure in failedWrites.Select(static write => write.IsCanceled ? "cancelled while waiting for room" : write.Exception?.InnerException?.GetType().Name ?? "still waiting").GroupBy(static failure => failure))
		{
			report.Log($"{failure.Count()} writes did not complete: {failure.Key}");
		}

		report.Log($"Accepted reads {sharedIngest.Accepted}, and QueueDepth reads {sharedIngest.QueueDepth}");

		// A write that never faulted and did not complete was waiting for room, which is a capacity problem rather than a lost reading
		var waitedForRoom = failedWrites.Any(static write => !write.IsFaulted);

		report.Expect(
			$"{_deviceCount} devices posting at the same instant have all {IngestPageBase.EventCount} readings accepted",
			$"{IngestPageBase.EventCount} accepted, {IngestPageBase.EventCount} queued",
			$"{sharedIngest.Accepted} accepted, {sharedIngest.QueueDepth} queued",
			sharedIngest.Accepted == IngestPageBase.EventCount && sharedIngest.QueueDepth == IngestPageBase.EventCount && failedWrites.Count is 0,
			waitedForRoom ? _capacityHint : _lostReadingsHint);

		// Part 5: a lost increment is rare with only 400 readings, so the code is checked too. The page reads these counters while consumers update them.
		var acceptAsync = typeof(TelemetryIngestService).GetMethod(nameof(TelemetryIngestService.AcceptAsync), [typeof(TelemetryEvent), typeof(CancellationToken)]);
		var acceptCounter = acceptAsync is null ? null : CodeInspector.GetCalledMethods(acceptAsync).FirstOrDefault(static call => call.StartsWith("Interlocked.", StringComparison.Ordinal));

		report.Expect(
			"AcceptAsync counts accepted readings with Interlocked",
			"Interlocked",
			acceptCounter ?? "no Interlocked call",
			acceptCounter is not null,
			"_accepted++ is a read, an add, and a write, and many devices call AcceptAsync at once. Update the counter atomically.");

		string[] counters = [nameof(TelemetryIngestService.Accepted), nameof(TelemetryIngestService.Processed)];
		var volatileCounters = counters
			.Where(static counter => typeof(TelemetryIngestService).GetProperty(counter)?.GetMethod is { } getter && CodeInspector.GetCalledMethods(getter).Contains("Volatile.Read"))
			.ToList();

		report.Expect(
			"Accepted and Processed read their counters with Volatile.Read",
			string.Join(", ", counters),
			volatileCounters.Count is 0 ? "neither" : string.Join(", ", volatileCounters),
			volatileCounters.Count == counters.Length,
			"An int read can be stale. The Blazor page reads these counters while consumers on other threads update them, so read the latest value rather than one cached in a register.");
	}

	// Waits a short while for a write that should stop, and describes how it ended
	static async Task<string> Outcome(Task write, CancellationToken token)
	{
		try
		{
			await write.WaitAsync(_writeTimeout, token).ConfigureAwait(false);
			return "completed";
		}
		catch (OperationCanceledException) when (!token.IsCancellationRequested)
		{
			return "cancelled";
		}
		catch (TimeoutException)
		{
			return $"was still waiting after {_writeTimeout.TotalSeconds} seconds";
		}
	}
}