using OrderPortal.Components.Pages;

namespace OrderPortal;

public sealed class Step2AddUpRevenue : WorkshopStep
{
	const int _threadCount = 32;
	const int _ordersPerThread = 25_000;
	const decimal _orderTotal = 1.25m;

	const string _lostRevenueHint = "Revenue went missing and nothing threw. decimal += is a much wider read, modify, write than int++, and Interlocked has no overload for decimal. "
		+ "Guard every write to the revenue, and every read of it, with the same lock.";

	public override int Number => 2;

	public override string Scenario => "Add up the revenue";

	public override string Title => "OrderMetrics.RecordOrder(), Revenue and Reset()";

	public override string Story => "At the end of the sale, finance reconciles the Revenue number against the payment processor. "
		+ "It is short, and by far more than the missing orders are worth. A 16 byte decimal update is a much wider window for two threads to collide in than an int, "
		+ "and the shortfall is different on every run, so a single run that happens to add up proves nothing.";

	public override string SeeItInTheApp => "On the Checkout page, the Revenue card is short too, and by considerably more than the missing orders are worth, often several times more. "
		+ "The two shortfalls never line up, no matter how carefully you divide.";

	public override string FileToChange => "Services/OrderMetrics.cs";

	public override IReadOnlyList<string> Tasks { get; } =
	[
		"Guard the revenue total. Interlocked cannot help you here, so pick the right lock for a synchronous update.",
		"Read Revenue under the same lock, so the page can never read a decimal that is halfway through an update.",
		"Keep Reset() correct. It clears the revenue at the start of every burst, so it needs the same lock.",
	];

	public override IReadOnlyList<string> Clues { get; } =
	[
		"Interlocked has overloads for int and long. It has none for decimal.",
		"A decimal is 16 bytes. A read can catch one halfway through an update.",
		".NET 9 added a dedicated Lock type, and the lock statement understands it.",
	];

	public override string TimeoutHint => "The checkout burst never finished. Check that every lock in OrderMetrics is released, and that no method takes a lock it already holds.";

	public override async Task Run(StepContext context)
	{
		var report = context.Report;
		var token = context.Token;

		// Part 1: every order in the burst has a known total, so the revenue has exactly one correct value
		var metrics = new OrderMetrics();
		using var ledger = new InventoryLedger();
		using var checkout = new CheckoutService(metrics, new TaxRateProvider(), ledger);

		report.Log($"Running {CheckoutPageBase.OrderCount:N0} checkouts through Parallel.ForEachAsync");
		var result = await checkout.RunCheckoutBurstAsync(CheckoutPageBase.OrderCount, token).ConfigureAwait(false);
		report.Log($"The burst finished in {result.Elapsed.TotalSeconds:F2}s, and Revenue reads {result.Revenue:C2}");

		report.Expect(
			"The checkout burst adds up to the expected revenue",
			CheckoutPageBase.ExpectedRevenue.ToString("C2"),
			result.Revenue.ToString("C2"),
			result.Revenue == CheckoutPageBase.ExpectedRevenue,
			_lostRevenueHint);

		// Part 2: 32 threads adding the same order total as fast as they can
		var stressMetrics = new OrderMetrics();
		var expectedRevenue = _threadCount * _ordersPerThread * _orderTotal;
		report.Log($"Recording {_threadCount * _ordersPerThread:N0} orders of {_orderTotal:C2} from {_threadCount} threads at the same time");

		Parallel.For(0, _threadCount, new ParallelOptions { MaxDegreeOfParallelism = _threadCount, CancellationToken = token }, _ =>
		{
			for (var order = 0; order < _ordersPerThread; order++)
			{
				stressMetrics.RecordOrder(_orderTotal);
			}
		});

		var stressRevenue = stressMetrics.Revenue;
		report.Log($"Revenue reads {stressRevenue:C2}");
		report.Expect(
			$"{_threadCount} threads adding {_ordersPerThread:N0} orders each lose none of the revenue",
			expectedRevenue.ToString("C2"),
			stressRevenue.ToString("C2"),
			stressRevenue == expectedRevenue,
			_lostRevenueHint);

		// Part 3: Reset() runs at the start of every burst, so it touches the same shared total
		stressMetrics.Reset();
		report.Expect("Reset() sets Revenue back to 0", 0m.ToString("C2"), stressMetrics.Revenue.ToString("C2"), stressMetrics.Revenue is 0, "Reset() runs at the start of every burst. It has to clear the revenue under the same lock that RecordOrder() uses to update it.");
	}
}