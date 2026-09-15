using OrderPortal.Components.Pages;

namespace OrderPortal;

public sealed class Step1CountEveryOrder : WorkshopStep
{
	const int _threadCount = 32;
	const int _ordersPerThread = 25_000;

	const string _lostOrdersHint = "Orders went missing and nothing threw. _ordersPlaced++ is a read, an add, and a write, and two threads can read the same value before either one writes. "
		+ "Update the count atomically, so nothing can slip between the read and the write.";

	public override int Number => 1;

	public override string Scenario => "Count every order";

	public override string Title => "OrderMetrics.RecordOrder() and OrdersPlaced";

	public override string Story => "A sale goes live and two thousand customers press Buy in the same second. "
		+ "Every checkout calls RecordOrder() on the one OrderMetrics singleton, and the Orders recorded number on the revenue report has to match the orders table exactly. "
		+ "When it does not, nothing throws and nothing logs a warning. The count is just quietly low.";

	public override string SeeItInTheApp => "On the Checkout page, press Run 2000 checkouts. The Orders recorded card expects 2000 and shows something slightly lower, such as 1996. "
		+ "Those orders were placed. The counter lost them. Press it again and the number changes, which is why a single passing run proves nothing.";

	public override string FileToChange => "Services/OrderMetrics.cs";

	public override IReadOnlyList<string> Tasks { get; } =
	[
		"Update the order count in RecordOrder() atomically, so no increment is ever lost.",
		"Make the OrdersPlaced getter safe too, so it cannot hand the page a stale count.",
		"Keep Reset() correct. It runs at the start of every burst, against the same shared count.",
		"Leave CheckoutService, the pages, and the singleton registrations in Program.cs alone. The load is not the bug.",
	];

	public override IReadOnlyList<string> Clues { get; } =
	[
		"_ordersPlaced++ is not one operation. It is a read, an add, and a write.",
		"Reads can be wrong as well as writes. An int read can be stale.",
		"The cheapest tool in this section's toolbox is lock free, so no thread ever waits.",
	];

	public override string TimeoutHint => "The checkout burst never finished. Check that nothing in OrderMetrics can block or wait on itself.";

	public override async Task Run(StepContext context)
	{
		var report = context.Report;
		var token = context.Token;

		// Part 1: the same burst the Checkout page runs, through fresh copies of the services Program.cs registers as singletons
		var metrics = new OrderMetrics();
		using var ledger = new InventoryLedger();
		using var checkout = new CheckoutService(metrics, new TaxRateProvider(), ledger);

		report.Log($"Running {CheckoutPageBase.OrderCount:N0} checkouts through Parallel.ForEachAsync, which runs one checkout per core ({Environment.ProcessorCount} on this machine)");
		var result = await checkout.RunCheckoutBurstAsync(CheckoutPageBase.OrderCount, token).ConfigureAwait(false);
		report.Log($"The burst finished in {result.Elapsed.TotalSeconds:F2}s, and OrdersPlaced reads {result.OrdersPlaced:N0}");

		report.Expect("The checkout burst records every order", CheckoutPageBase.OrderCount, result.OrdersPlaced, _lostOrdersHint);

		// Part 2: one burst can get lucky. 32 threads calling RecordOrder() as fast as they can almost never do.
		var stressMetrics = new OrderMetrics();
		report.Log($"Recording {_threadCount * _ordersPerThread:N0} orders from {_threadCount} threads at the same time");

		Parallel.For(0, _threadCount, new ParallelOptions { MaxDegreeOfParallelism = _threadCount, CancellationToken = token }, _ =>
		{
			for (var order = 0; order < _ordersPerThread; order++)
			{
				stressMetrics.RecordOrder(1m);
			}
		});

		report.Log($"OrdersPlaced reads {stressMetrics.OrdersPlaced:N0}");
		report.Expect($"{_threadCount} threads recording {_ordersPerThread:N0} orders each lose none of them", _threadCount * _ordersPerThread, stressMetrics.OrdersPlaced, _lostOrdersHint);

		// Part 3: Reset() runs at the start of every burst, so it touches the same shared count
		stressMetrics.Reset();
		report.Expect("Reset() sets OrdersPlaced back to 0", 0, stressMetrics.OrdersPlaced, "Reset() runs at the start of every burst. It has to clear the same shared count that RecordOrder() updates.");
	}
}