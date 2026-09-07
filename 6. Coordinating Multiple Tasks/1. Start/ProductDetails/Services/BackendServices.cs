namespace ProductDetails;

// Each service stands in for a real downstream call: a database, a pricing
// engine, a reviews API. The delays are what those calls actually cost.
public sealed class InventoryService
{
	public async Task<InventoryInfo> GetInventoryAsync(string sku, CancellationToken token)
	{
		await Task.Delay(TimeSpan.FromMilliseconds(700), token).ConfigureAwait(false);

		return new InventoryInfo(42, "Reno, NV");
	}
}

public sealed class PricingService
{
	public async Task<PricingInfo> GetPricingAsync(string sku, CancellationToken token)
	{
		await Task.Delay(TimeSpan.FromMilliseconds(900), token).ConfigureAwait(false);

		return new PricingInfo(129.99m, 109.99m);
	}
}

public sealed class ReviewsService
{
	public async Task<ReviewsInfo> GetReviewsAsync(string sku, CancellationToken token)
	{
		await Task.Delay(TimeSpan.FromMilliseconds(1_200), token).ConfigureAwait(false);

		return new ReviewsInfo(4.6, 1_284);
	}
}

public sealed class ShippingService
{
	public async Task<ShippingInfo> GetShippingAsync(string sku, CancellationToken token)
	{
		await Task.Delay(TimeSpan.FromMilliseconds(600), token).ConfigureAwait(false);

		return new ShippingInfo("UPS Ground", DateOnly.FromDateTime(DateTime.Today.AddDays(3)));
	}
}

public sealed class RecommendationsService
{
	// The recommendations engine is the flaky one. It fails often enough that
	// the page must survive without it.
	public async Task<RecommendationsInfo> GetRecommendationsAsync(string sku, CancellationToken token)
	{
		await Task.Delay(TimeSpan.FromMilliseconds(800), token).ConfigureAwait(false);

		throw new HttpRequestException("Recommendations service returned 503 Service Unavailable");
	}
}