using ProductDetails.Components;

namespace ProductDetails;

public static class Program
{
	public static void Main(string[] args)
	{
		var builder = WebApplication.CreateBuilder(args);

		builder.Services.AddRazorComponents()
			.AddInteractiveServerComponents();

		// Five independent backend services, the way a real product page fans out
		builder.Services.AddSingleton<InventoryService>();
		builder.Services.AddSingleton<PricingService>();
		builder.Services.AddSingleton<ReviewsService>();
		builder.Services.AddSingleton<ShippingService>();
		builder.Services.AddSingleton<RecommendationsService>();

		var app = builder.Build();

		if (!app.Environment.IsDevelopment())
		{
			app.UseExceptionHandler("/Error");
			app.UseHsts();
			app.UseHttpsRedirection();
		}

		app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
		app.UseAntiforgery();

		app.MapStaticAssets();
		app.MapRazorComponents<App>()
			.AddInteractiveServerRenderMode();

		app.Run();
	}
}