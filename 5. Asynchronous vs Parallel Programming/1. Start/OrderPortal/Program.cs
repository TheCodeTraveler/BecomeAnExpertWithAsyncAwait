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
		// concurrent request. That is what makes their state a shared resource.
		builder.Services.AddSingleton<OrderMetrics>();
		builder.Services.AddSingleton<TaxRateProvider>();
		builder.Services.AddSingleton<InventoryLedger>();
		builder.Services.AddSingleton<CheckoutService>();

		// Workshop plumbing: one StepVerifier holds every step's result. It is also a hosted service,
		// so it checks your services against every step each time the app starts.
		builder.Services.AddSingleton<StepVerifier>();
		builder.Services.AddHostedService(static serviceProvider => serviceProvider.GetRequiredService<StepVerifier>());

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