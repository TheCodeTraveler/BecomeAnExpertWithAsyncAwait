namespace OrderPortal;

// Tax tables are expensive to build, so we build them once and cache them.
// At least, that is the intent.
public sealed class TaxRateProvider
{
	int _lookups;
	int _builds;

	// ToDo Refactor: two threads can both find this null and both build the table
	IReadOnlyDictionary<string, decimal>? _rates;

	public int Lookups => _lookups;

	// How many times the expensive table was actually built. Should be 1.
	public int Builds => _builds;

	public decimal GetRate(string region)
	{
		_lookups++;

		// ToDo Refactor: `??=` is not atomic. Under load this runs BuildRates()
		// many times, and every caller pays the full build cost.
		_rates ??= BuildRates();

		return _rates.TryGetValue(region, out var rate) ? rate : 0m;
	}

	public void Reset()
	{
		_rates = null;
		_lookups = 0;
		_builds = 0;
	}

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