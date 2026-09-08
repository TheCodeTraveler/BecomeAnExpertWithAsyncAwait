namespace OrderPortal;

// Tax tables are expensive to build, so we build them once and cache them.
public sealed class TaxRateProvider
{
	int _lookups;
	int _builds;

	// Lazy<T> guarantees the factory runs exactly once no matter how many threads
	// hit Value at the same time. That is its default thread safety mode,
	// LazyThreadSafetyMode.ExecutionAndPublication.
	Lazy<IReadOnlyDictionary<string, decimal>> _rates;

	public TaxRateProvider()
	{
		_rates = CreateRatesLazy();
	}

	public int Lookups => Volatile.Read(ref _lookups);

	// How many times the expensive table was actually built. Always 1.
	public int Builds => Volatile.Read(ref _builds);

	public decimal GetRate(string region)
	{
		Interlocked.Increment(ref _lookups);

		return _rates.Value.TryGetValue(region, out var rate) ? rate : 0m;
	}

	public void Reset()
	{
		Interlocked.Exchange(ref _lookups, 0);
		Interlocked.Exchange(ref _builds, 0);

		_rates = CreateRatesLazy();
	}

	Lazy<IReadOnlyDictionary<string, decimal>> CreateRatesLazy() => new(BuildRates);

	IReadOnlyDictionary<string, decimal> BuildRates()
	{
		Interlocked.Increment(ref _builds);

		// Pretend this reads a rate table from the database
		Thread.Sleep(TimeSpan.FromMilliseconds(120));

		return new Dictionary<string, decimal>
		{
			{ "US-CA", 0.0925m },
			{ "US-NY", 0.08875m },
			{ "US-TX", 0.0825m },
			{ "US-WA", 0.101m },
		};
	}
}