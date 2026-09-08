using System.Diagnostics;
using Microsoft.AspNetCore.Components;

namespace ProductDetails.Components.Pages;

public partial class ProductPageBase : ComponentBase
{
	const string _sku = "SKU-1000";

	[Inject]
	public required InventoryService InventoryService { get; init; }

	[Inject]
	public required PricingService PricingService { get; init; }

	[Inject]
	public required ReviewsService ReviewsService { get; init; }

	[Inject]
	public required ShippingService ShippingService { get; init; }

	[Inject]
	public required RecommendationsService RecommendationsService { get; init; }

	[Inject]
	public required ILogger<ProductPageBase> Logger { get; init; }

	public bool IsLoading { get; private set; }

	public double? TotalSeconds { get; private set; }

	public string? PageError { get; private set; }

	public List<PanelState> Panels { get; } =
	[
		PanelState.Waiting("Inventory"),
		PanelState.Waiting("Pricing"),
		PanelState.Waiting("Reviews"),
		PanelState.Waiting("Shipping"),
		PanelState.Waiting("Recommendations"),
	];

	protected override async Task OnInitializedAsync() => await LoadProductAsync().ConfigureAwait(false);

	protected async Task LoadProductAsync()
	{
		IsLoading = true;
		TotalSeconds = null;
		PageError = null;
		ResetPanels();

		await InvokeAsync(StateHasChanged).ConfigureAwait(false);

		var stopwatch = Stopwatch.StartNew();

		try
		{
			// ToDo Refactor: these five services do not depend on each other, but
			// each await waits for the previous one to finish. The page costs the
			// sum of every latency instead of the slowest one.
			var inventory = await InventoryService.GetInventoryAsync(_sku, CancellationToken.None).ConfigureAwait(false);
			await SetPanelAsync("Inventory", "ready", $"{inventory.InStock} in stock at {inventory.Warehouse}", stopwatch.Elapsed).ConfigureAwait(false);

			var pricing = await PricingService.GetPricingAsync(_sku, CancellationToken.None).ConfigureAwait(false);
			await SetPanelAsync("Pricing", "ready", $"{pricing.YourPrice:C} (list {pricing.ListPrice:C})", stopwatch.Elapsed).ConfigureAwait(false);

			var reviews = await ReviewsService.GetReviewsAsync(_sku, CancellationToken.None).ConfigureAwait(false);
			await SetPanelAsync("Reviews", "ready", $"{reviews.AverageRating:F1} stars from {reviews.ReviewCount:N0} reviews", stopwatch.Elapsed).ConfigureAwait(false);

			var shipping = await ShippingService.GetShippingAsync(_sku, CancellationToken.None).ConfigureAwait(false);
			await SetPanelAsync("Shipping", "ready", $"{shipping.Carrier}, arrives {shipping.EstimatedArrival:MMM d}", stopwatch.Elapsed).ConfigureAwait(false);

			// ToDo Refactor: this service is down. Every call shares one try block,
			// so its failure is the whole page's failure. Move it above Reviews and
			// two more panels go blank. One flaky service should degrade one panel.
			var recommendations = await RecommendationsService.GetRecommendationsAsync(_sku, CancellationToken.None).ConfigureAwait(false);
			await SetPanelAsync("Recommendations", "ready", string.Join(", ", recommendations.AlsoBought), stopwatch.Elapsed).ConfigureAwait(false);
		}
		catch (HttpRequestException e)
		{
			// The full exception goes to the log, where it can be acted on. The banner
			// gets a fixed message the page owns, so no exception text reaches the browser.
			Logger.LogError(e, "The product page load stopped because a backend service failed.");

			// The continuation is off Blazor's renderer, so these writes go back through it
			await InvokeAsync(() =>
			{
				PageError = "A backend service did not respond. Every panel below it was never requested.";

				// Anything still waiting when the load stopped will never arrive
				MarkWaitingPanelsSkipped();
			}).ConfigureAwait(false);
		}
		finally
		{
			stopwatch.Stop();

			await InvokeAsync(() =>
			{
				TotalSeconds = stopwatch.Elapsed.TotalSeconds;
				IsLoading = false;

				StateHasChanged();
			}).ConfigureAwait(false);
		}
	}

	void MarkWaitingPanelsSkipped()
	{
		for (var index = 0; index < Panels.Count; index++)
		{
			if (Panels[index].Status is "waiting")
			{
				Panels[index] = Panels[index] with { Status = "skipped" };
			}
		}
	}

	protected void ResetPanels()
	{
		for (var index = 0; index < Panels.Count; index++)
		{
			Panels[index] = PanelState.Waiting(Panels[index].Name);
		}
	}

	// Product.razor renders Panels with a foreach, and every write here arrives
	// from a service continuation rather than from the renderer. Writing straight
	// to the List<T> would bump its version mid-render, so the write is marshalled.
	protected Task SetPanelAsync(string name, string status, string? detail, TimeSpan elapsed) =>
		InvokeAsync(() =>
		{
			var index = Panels.FindIndex(panel => panel.Name == name);

			if (index >= 0)
			{
				Panels[index] = new PanelState(name, status, detail, elapsed.TotalSeconds);
			}
		});
}