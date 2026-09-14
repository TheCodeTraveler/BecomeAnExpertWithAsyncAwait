using CoffeeShop.Components;

namespace CoffeeShop;

public static class Program
{
	public static void Main(string[] args)
	{
		var builder = WebApplication.CreateBuilder(args);

		builder.Services.AddRazorComponents()
			.AddInteractiveServerComponents();

		// One StepVerifier holds every step's result. It is also a hosted service,
		// so it checks your CustomTask against Steps 1 to 5 every time the app starts.
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