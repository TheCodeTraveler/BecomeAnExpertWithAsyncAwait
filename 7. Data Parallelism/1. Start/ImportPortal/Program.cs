using ImportPortal.Components;

namespace ImportPortal;

public static class Program
{
	public static void Main(string[] args)
	{
		var builder = WebApplication.CreateBuilder(args);

		builder.Services.AddRazorComponents()
			.AddInteractiveServerComponents();

		builder.Services.AddSingleton<OrderFileService>();
		builder.Services.AddSingleton<CustomerApiService>();
		builder.Services.AddSingleton<ImportService>();

		// Workshop plumbing: one StepVerifier holds every step's result. It is also a hosted service,
		// so it checks your ImportService against every step each time the app starts.
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