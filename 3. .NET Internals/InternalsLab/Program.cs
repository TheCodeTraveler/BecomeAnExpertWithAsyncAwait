using InternalsLab.Components;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace InternalsLab;

public static class Program
{
	public static void Main(string[] args)
	{
		var builder = WebApplication.CreateBuilder(args);

		builder.Services.AddRazorComponents()
			.AddInteractiveServerComponents();

		// Step 3: the Principal experiment is an MVC controller, because it has to observe a real ASP.NET Core request
		builder.Services.AddControllersWithViews();
		builder.Services.AddHttpContextAccessor();
		builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
			.AddCookie(static options => options.LoginPath = "/Account/SignIn");

		// Workshop plumbing: one LabNotebook keeps your predictions, results and answers for as long as the app runs,
		// so reloading a page, or leaving the Blazor page for the Step 3 experiment, never loses them
		builder.Services.AddSingleton<LabNotebook>();

		var app = builder.Build();

		if (!app.Environment.IsDevelopment())
		{
			app.UseExceptionHandler("/Error");
			app.UseHsts();
			app.UseHttpsRedirection();
		}

		app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

		// Step 3: reads the sign-in cookie on every request and assigns the signed-in user to HttpContext.User
		app.UseAuthentication();
		app.UseAuthorization();
		app.UseAntiforgery();

		app.MapStaticAssets();
		app.MapDefaultControllerRoute();
		app.MapRazorComponents<App>()
			.AddInteractiveServerRenderMode();

		app.Run();
	}
}