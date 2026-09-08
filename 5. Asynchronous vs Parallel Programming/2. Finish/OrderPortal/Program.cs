using OrderPortal.Components;

namespace OrderPortal;

public static class Program
{
	public static void Main(string[] args)
	{
		var builder = WebApplication.CreateBuilder(args);

		builder.Services.AddRazorComponents()
			.AddInteractiveServerComponents();

		// Registered as singletons, so one instance is shared by every
		// concurrent request. That ensures their state is a shared resource.
		builder.Services.AddSingleton<OrderMetrics>();
		builder.Services.AddSingleton<TaxRateProvider>();
		builder.Services.AddSingleton<InventoryLedger>();
		builder.Services.AddSingleton<CheckoutService>();

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