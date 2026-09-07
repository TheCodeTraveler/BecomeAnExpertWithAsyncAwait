namespace ImportPortal;

// A downstream customer API. Real ones are slow and rate limited, which is why
// the enrichment step must bound how many calls it makes at once.
public sealed class CustomerApiService
{
	static readonly string[] _tiers = ["Bronze", "Silver", "Gold", "Platinum"];

	public async Task<string> GetCustomerTierAsync(string sku, CancellationToken token)
	{
		await Task.Delay(TimeSpan.FromMilliseconds(10), token).ConfigureAwait(false);

		return _tiers[Math.Abs(sku.GetHashCode(StringComparison.Ordinal)) % _tiers.Length];
	}
}