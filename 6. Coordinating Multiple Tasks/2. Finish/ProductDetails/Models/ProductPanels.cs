namespace ProductDetails;

public record InventoryInfo(int InStock, string Warehouse);

public record PricingInfo(decimal ListPrice, decimal YourPrice);

public record ReviewsInfo(double AverageRating, int ReviewCount);

public record ShippingInfo(string Carrier, DateOnly EstimatedArrival);

public record RecommendationsInfo(IReadOnlyList<string> AlsoBought);

// One panel's worth of the page. Panels arrive independently.
public record PanelState(string Name, string Status, string? Detail, double? Seconds)
{
	public static PanelState Waiting(string name) => new(name, "waiting", null, null);
}