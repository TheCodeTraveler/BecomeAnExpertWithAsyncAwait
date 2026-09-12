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

	public List<PanelState> Panels { get; } =
	[
		PanelState.Waiting("Inventory"),
		PanelState.Waiting("Pricing"),
		PanelState.Waiting("Reviews"),
		PanelState.Waiting("Shipping"),
		PanelState.Waiting("Recommendations"),
	];

	// Every panel handles its own failure, so the page itself never fails
	public string? PageError => null;

	protected override async Task OnInitializedAsync() => await LoadProductAsync().ConfigureAwait(false);

	protected async Task LoadProductAsync()
	{
		IsLoading = true;
		TotalSeconds = null;
		ResetPanels();

		await InvokeAsync(StateHasChanged).ConfigureAwait(false);

		var stopwatch = Stopwatch.StartNew();

		// Start every call before awaiting any of them. From here the page
		// costs the slowest service, not the sum of all five.
		var panelTasks = new List<Task>
		{
			TrackPanelAsync("Inventory", InventoryService.GetInventoryAsync(_sku, CancellationToken.None),
				static inventory => $"{inventory.InStock} in stock at {inventory.Warehouse}", stopwatch),

			TrackPanelAsync("Pricing", PricingService.GetPricingAsync(_sku, CancellationToken.None),
				static pricing => $"{pricing.YourPrice:C} (list {pricing.ListPrice:C})", stopwatch),

			TrackPanelAsync("Reviews", ReviewsService.GetReviewsAsync(_sku, CancellationToken.None),
				static reviews => $"{reviews.AverageRating:F1} stars from {reviews.ReviewCount:N0} reviews", stopwatch),

			TrackPanelAsync("Shipping", ShippingService.GetShippingAsync(_sku, CancellationToken.None),
				static shipping => $"{shipping.Carrier}, arrives {shipping.EstimatedArrival:MMM d}", stopwatch),

			TrackPanelAsync("Recommendations", RecommendationsService.GetRecommendationsAsync(_sku, CancellationToken.None),
				static recommendations => string.Join(", ", recommendations.AlsoBought), stopwatch),
		};

		// Task.WhenEach streams each task as it finishes, so every panel paints
		// the moment its own service answers instead of waiting for the slowest.
		await foreach (var finishedPanel in Task.WhenEach(panelTasks))
		{
			// Each task already recorded its own panel and logged its own
			// failure, so awaiting here only observes completion.
			await finishedPanel.ConfigureAwait(false);

			await InvokeAsync(StateHasChanged).ConfigureAwait(false);
		}

		stopwatch.Stop();

		await InvokeAsync(() =>
		{
			TotalSeconds = stopwatch.Elapsed.TotalSeconds;
			IsLoading = false;

			StateHasChanged();
		}).ConfigureAwait(false);
	}

	protected void ResetPanels()
	{
		for (var index = 0; index < Panels.Count; index++)
		{
			Panels[index] = PanelState.Waiting(Panels[index].Name);
		}
	}

	// All five wrappers write here at once, from whichever Thread Pool thread
	// their own service finished on, while Product.razor renders Panels with a
	// foreach. Marshalling the write keeps every mutation on the renderer.
	protected Task SetPanelAsync(string name, string status, string? detail, TimeSpan elapsed) =>
		InvokeAsync(() =>
		{
			var index = Panels.FindIndex(panel => panel.Name == name);

			if (index >= 0)
			{
				Panels[index] = new PanelState(name, status, detail, elapsed.TotalSeconds);
			}
		});

	// Wraps one backend call so a single failing service degrades one panel
	// instead of taking down the whole page.
	async Task TrackPanelAsync<T>(string name, Task<T> serviceCall, Func<T, string> describe, Stopwatch stopwatch)
	{
		try
		{
			var result = await serviceCall.ConfigureAwait(false);

			await SetPanelAsync(name, "ready", describe(result), stopwatch.Elapsed).ConfigureAwait(false);
		}
		catch (HttpRequestException e)
		{
			// The full exception goes to the log, where it can be acted on. The card
			// gets a fixed message the page owns, so no exception text reaches the browser.
			Logger.LogError(e, "The {PanelName} service failed while loading the product page.", name);

			await SetPanelAsync(name, "failed", "The service did not respond. Try again in a moment.", stopwatch.Elapsed).ConfigureAwait(false);
		}
	}
}