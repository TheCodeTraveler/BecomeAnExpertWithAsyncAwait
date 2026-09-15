namespace ImportPortal;

// A downstream customer API. Real ones are slow and rate limited, which is why
// the enrichment step must bound how many calls it makes at once.
public sealed class CustomerApiService
{
	static readonly string[] _tiers = ["Bronze", "Silver", "Gold", "Platinum"];

	// Workshop instrumentation: the workshop steps read these counters to see how many calls
	// ran at the same time. You do not need to change this file.
	int _callsInFlight;
	int _peakCallsInFlight;
	int _callsCompleted;

	public int CallsInFlight => Volatile.Read(ref _callsInFlight);

	public int PeakCallsInFlight => Volatile.Read(ref _peakCallsInFlight);

	public int CallsCompleted => Volatile.Read(ref _callsCompleted);

	public async Task<string> GetCustomerTierAsync(string sku, CancellationToken token)
	{
		RecordCallStarted();

		try
		{
			await Task.Delay(TimeSpan.FromMilliseconds(10), token).ConfigureAwait(false);

			Interlocked.Increment(ref _callsCompleted);

			return _tiers[Math.Abs(sku.GetHashCode(StringComparison.Ordinal)) % _tiers.Length];
		}
		finally
		{
			Interlocked.Decrement(ref _callsInFlight);
		}
	}

	void RecordCallStarted()
	{
		var callsInFlight = Interlocked.Increment(ref _callsInFlight);

		// Raise the peak only if no other call raised it higher in the meantime
		var peak = Volatile.Read(ref _peakCallsInFlight);

		while (callsInFlight > peak && Interlocked.CompareExchange(ref _peakCallsInFlight, callsInFlight, peak) != peak)
		{
			peak = Volatile.Read(ref _peakCallsInFlight);
		}
	}
}