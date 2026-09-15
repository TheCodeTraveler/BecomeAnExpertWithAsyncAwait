namespace OrderPortal;

public sealed class Step4ReserveStock : WorkshopStep
{
	const string _sku = "SKU-1000";
	const int _stockOnHand = 5_000;
	const int _reservationCount = 200;

	const string _deadlockHint = "No thread is blocked and nothing threw, because WaitAsync is awaited. The ledger's only permit is simply never released. "
		+ "SemaphoreSlim is not reentrant: ask which method takes the permit, and which method it calls next. Keep the stock update and its audit entry under one lock, without taking that lock twice.";

	const string _skippedHint = "The ledger is still holding its only permit, so every later call on it would wait too. Fix the deadlock first.";

	// A real request is eventually abandoned by its client. These calls give up after 2 seconds, so a deadlock ends this step instead of hanging it.
	static readonly TimeSpan _ledgerTimeout = TimeSpan.FromSeconds(2);

	public override int Number => 4;

	public override string Scenario => "Reserve stock without waiting on yourself";

	public override string Title => "InventoryLedger.ReserveStockAsync() and WriteAuditEntryAsync()";

	public override string Story => "Every reservation has to update the stock and write its audit entry under one lock, so the ledger can never show stock leaving the warehouse without a record of why. "
		+ "InventoryLedger uses SemaphoreSlim, which is the right lock for code that awaits. Reserve stock still never answers. "
		+ "Nothing is pegged, nothing throws, and a thread dump shows nothing waiting. The request just never completes, and every later reservation queues up behind it.";

	public override string SeeItInTheApp => "On the Checkout page, press Reserve stock. The panel says Reserving stock... and then sits there. "
		+ "Five seconds later it gives up with a timeout message. Nothing crashed. The request is simply stuck.";

	public override string FileToChange => "Services/InventoryLedger.cs";

	public override IReadOnlyList<string> Tasks { get; } =
	[
		"Remove the deadlock in ReserveStockAsync() without losing the guarantee that the stock update and its audit entry happen under the same lock.",
		"Keep WriteAuditEntryAsync() callable on its own, by a caller that does not already hold the ledger lock.",
		"Guard the audit trail list with a lock, so AuditEntries never reads it while an entry is being written.",
		"Keep SemaphoreSlim in InventoryLedger. lock cannot be held across an await, and the ledger awaits.",
	];

	public override IReadOnlyList<string> Clues { get; } =
	[
		"InventoryLedger uses SemaphoreSlim rather than lock, and that part is correct.",
		"SemaphoreSlim is not reentrant. Ask which method takes the permit, and which method it calls next.",
		"A common pattern: a public method that takes the lock, and a private method that assumes the lock is already held.",
		"List<T> is not thread safe, and a lock is all this one needs.",
	];

	public override string TimeoutHint => "A ledger call ignored its 2 second timeout. Check that every wait on the ledger's semaphore passes the CancellationToken it was given.";

	public override async Task Run(StepContext context)
	{
		var report = context.Report;
		var token = context.Token;

		using var ledger = new InventoryLedger();

		// Part 1: one reservation, the same call the Reserve stock button makes
		report.Log($"Reserving 1 of {_sku}");

		var (answered, reserved) = await WithinLedgerTimeout(timeoutToken => ledger.ReserveStockAsync(_sku, 1, timeoutToken), token).ConfigureAwait(false);

		if (!answered)
		{
			report.Log("Still waiting for the ledger lock after 2 seconds. Giving up, the way a client would");
			report.Expect("Reserving 1 unit answers within 2 seconds", "True, within 2 seconds", "no answer after 2 seconds", false, _deadlockHint);
			report.Expect("WriteAuditEntryAsync() works on its own", "completed", "Skipped: the ledger is deadlocked", false, _skippedHint);
			report.Expect($"{_reservationCount} reservations at the same time all succeed", $"{_reservationCount} of {_reservationCount}", "Skipped: the ledger is deadlocked", false, _skippedHint);
			return;
		}

		report.Log($"ReserveStockAsync() answered {reserved}");
		report.Expect("Reserving 1 unit answers within 2 seconds", "True, within 2 seconds", $"{reserved}, within 2 seconds", reserved, _deadlockHint);
		report.Expect("The reservation wrote its audit entry", 1, ledger.AuditEntries, "Every reservation has to write exactly one audit entry, inside the same lock as the stock update.");

		// Part 2: a caller that does not hold the ledger lock can still write an audit entry on its own
		report.Log("Writing a cycle count adjustment to the audit trail on its own");

		var (wroteEntry, _) = await WithinLedgerTimeout(async timeoutToken =>
		{
			await ledger.WriteAuditEntryAsync("Cycle count adjustment", timeoutToken).ConfigureAwait(false);
			return true;
		}, token).ConfigureAwait(false);

		report.Expect(
			"WriteAuditEntryAsync() works on its own, for a caller that does not already hold the ledger lock",
			"completed, 2 audit entries",
			wroteEntry ? $"completed, {ledger.AuditEntries} audit entries" : "no answer after 2 seconds",
			wroteEntry && ledger.AuditEntries is 2,
			"WriteAuditEntryAsync() still has to take the ledger lock itself, because its caller does not hold it.");

		// Part 3: a burst of reservations at the same time. The semaphore lets them in one at a time.
		report.Log($"Placing {_reservationCount} reservations for {_sku} at the same time");

		var (reservationsAnswered, reservations) = await WithinLedgerTimeout(
			timeoutToken => Task.WhenAll(Enumerable.Range(0, _reservationCount).Select(_ => ledger.ReserveStockAsync(_sku, 1, timeoutToken))),
			token).ConfigureAwait(false);

		var succeeded = reservations?.Count(static reservation => reservation) ?? 0;
		report.Log($"{succeeded} reservations succeeded, and the audit trail has {ledger.AuditEntries} entries");

		report.Expect($"{_reservationCount} reservations at the same time all succeed", $"{_reservationCount} of {_reservationCount}", reservationsAnswered ? $"{succeeded} of {_reservationCount}" : "no answer after 2 seconds", succeeded is _reservationCount, _deadlockHint);
		report.Expect("Each of them wrote one audit entry", 2 + _reservationCount, ledger.AuditEntries, "Every reservation has to write exactly one audit entry, inside the same lock as the stock update.");

		// Part 4: 201 units have been reserved, so there is no longer enough stock for the whole shelf
		var reservedSoFar = 1 + _reservationCount;
		var (tooManyAnswered, tooMany) = await WithinLedgerTimeout(timeoutToken => ledger.ReserveStockAsync(_sku, _stockOnHand, timeoutToken), token).ConfigureAwait(false);

		report.Expect(
			$"Reserving all {_stockOnHand:N0} again is refused, because {reservedSoFar} are already reserved",
			"False",
			tooManyAnswered ? tooMany.ToString() : "no answer after 2 seconds",
			tooManyAnswered && !tooMany,
			"Every successful reservation has to reduce the stock on hand.");
	}

	// Answered is false when the call is still waiting after 2 seconds
	static async Task<(bool Answered, T? Value)> WithinLedgerTimeout<T>(Func<CancellationToken, Task<T>> call, CancellationToken token)
	{
		using var timeoutCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(token);
		timeoutCancellationTokenSource.CancelAfter(_ledgerTimeout);

		try
		{
			return (true, await call(timeoutCancellationTokenSource.Token).ConfigureAwait(false));
		}
		catch (OperationCanceledException) when (timeoutCancellationTokenSource.IsCancellationRequested && !token.IsCancellationRequested)
		{
			return (false, default);
		}
	}
}