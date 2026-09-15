using OrderPortal.Components.Pages;

namespace OrderPortal;

public sealed class Step3BuildTaxTableOnce : WorkshopStep
{
	const int _threadCount = 32;
	const int _lookupsPerThread = 10_000;
	const string _region = "US-CA";
	const decimal _expectedRate = 0.0925m;

	const string _buildsHint = "_rates ??= BuildRates() is a null check followed by an assignment, with a 120 millisecond gap in between. Every thread that asks for a rate during that gap finds the cache empty and builds its own table. "
		+ "Build the table exactly once, no matter how many threads call GetRate() at the same moment.";

	const string _lookupsHint = "Lookups went missing and nothing threw. _lookups++ is a read, an add, and a write, the same bug you fixed in OrderMetrics.";

	const string _resetHint = "Reset() runs at the start of every burst. After it, the provider has to build the table again on the next GetRate(), and still build it exactly once.";

	public override int Number => 3;

	public override string Scenario => "Build the tax table once";

	public override string Title => "TaxRateProvider.GetRate(), Lookups, Builds and Reset()";

	public override string Story => "Building the tax table takes 120 milliseconds, so TaxRateProvider builds it once and caches it for the life of the process. At least, that is the intent. "
		+ "When the first burst of a sale arrives, every checkout that asks for a rate during those 120 milliseconds finds the cache empty and pays for a table of its own. "
		+ "The number of tables you build turns out to be your core count.";

	public override string SeeItInTheApp => "On the Checkout page, the Tax table builds card expects 1 and shows one build per CPU core, so 16 on a 16 core machine and 8 on an 8 core laptop. "
		+ "The note under Elapsed counts tax rate lookups, and that counter is a plain ++ as well.";

	public override string FileToChange => "Services/TaxRateProvider.cs";

	public override IReadOnlyList<string> Tasks { get; } =
	[
		"Build the expensive tax table exactly once, no matter how many threads call GetRate() at the same moment.",
		"Count tax rate lookups atomically, and read Lookups and Builds so the page never sees a stale value.",
		"Keep Reset() working. After a reset, the next burst must build the table again, exactly once.",
	];

	public override IReadOnlyList<string> Clues { get; } =
	[
		"_rates ??= BuildRates() is a null check followed by an assignment, with a gap in between.",
		"One of the types in this section's toolbox exists to run a factory exactly once, however many threads ask for its value at the same time.",
		"Reset() does not have to undo a table that was already built. It only has to leave the provider ready to build a fresh one.",
	];

	public override string TimeoutHint => "GetRate() never returned. Check that the first caller builds the table and every other caller can read it once it is built.";

	public override async Task Run(StepContext context)
	{
		var report = context.Report;
		var token = context.Token;

		// Part 1: 32 threads wait at a Barrier, then all ask for a rate at the same instant, the way the first checkouts of a sale do
		var taxRates = new TaxRateProvider();
		report.Log($"{_threadCount} threads are asking for the {_region} tax rate at the same instant");

		var rates = AskForRatesAtTheSameTime(taxRates);
		report.Log($"The table was built {taxRates.Builds} times");

		report.Expect($"{_threadCount} threads asking for a rate at the same instant build the table once", 1, taxRates.Builds, _buildsHint);

		var correctRates = rates.Count(static rate => rate is _expectedRate);
		report.Expect($"Every thread gets the {_region} rate of {_expectedRate}", $"{_threadCount} of {_threadCount}", $"{correctRates} of {_threadCount}", correctRates is _threadCount, "Every caller has to read the same table. Check what GetRate() returns while the table is still being built.");

		// Part 2: the table is built, and now every checkout looks up a rate as fast as it can
		report.Log($"{_threadCount} threads are each looking up {_lookupsPerThread:N0} rates");

		Parallel.For(0, _threadCount, new ParallelOptions { MaxDegreeOfParallelism = _threadCount, CancellationToken = token }, _ =>
		{
			for (var lookup = 0; lookup < _lookupsPerThread; lookup++)
			{
				taxRates.GetRate(_region);
			}
		});

		var expectedLookups = _threadCount + (_threadCount * _lookupsPerThread);
		report.Log($"Lookups reads {taxRates.Lookups:N0}");
		report.Expect("Every lookup is counted", expectedLookups, taxRates.Lookups, _lookupsHint);

		// Part 3: after Reset() the next burst must build the table again, exactly once
		taxRates.Reset();
		report.Expect("Reset() sets Builds and Lookups back to 0", "0 builds, 0 lookups", $"{taxRates.Builds} builds, {taxRates.Lookups} lookups", taxRates.Builds is 0 && taxRates.Lookups is 0, _resetHint);

		AskForRatesAtTheSameTime(taxRates);
		report.Log($"After Reset(), the table was built {taxRates.Builds} times");
		report.Expect($"After Reset(), {_threadCount} threads asking at the same instant build the table once again", 1, taxRates.Builds, _resetHint);

		// Part 4: the burst the Checkout page runs resets the provider first, then shows the same number on the Tax table builds card
		var metrics = new OrderMetrics();
		using var ledger = new InventoryLedger();
		using var checkout = new CheckoutService(metrics, new TaxRateProvider(), ledger);

		report.Log($"Running {CheckoutPageBase.OrderCount:N0} checkouts through Parallel.ForEachAsync");
		var result = await checkout.RunCheckoutBurstAsync(CheckoutPageBase.OrderCount, token).ConfigureAwait(false);

		report.Expect("The checkout burst builds the tax table once", 1, result.TaxRateBuilds, _buildsHint);
	}

	static decimal[] AskForRatesAtTheSameTime(TaxRateProvider taxRates)
	{
		var rates = new decimal[_threadCount];
		var exceptions = new Exception?[_threadCount];

		using var barrier = new Barrier(_threadCount);

		var threads = Enumerable.Range(0, _threadCount)
			.Select(index => new Thread(() =>
			{
				barrier.SignalAndWait();

				// An exception on a dedicated thread would end the whole app, so keep it and rethrow it from this method instead
				try
				{
					rates[index] = taxRates.GetRate(_region);
				}
				catch (Exception e)
				{
					exceptions[index] = e;
				}
			}))
			.ToList();

		threads.ForEach(static thread => thread.Start());
		threads.ForEach(static thread => thread.Join());

		if (exceptions.FirstOrDefault(static exception => exception is not null) is { } firstException)
			throw new InvalidOperationException("TaxRateProvider.GetRate() threw", firstException);

		return rates;
	}
}