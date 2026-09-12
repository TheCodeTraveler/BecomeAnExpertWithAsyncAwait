using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace PrincipalExample.Controllers;

public class AccountController(IHttpContextAccessor httpContextAccessor, ILogger<AccountController> logger) : Controller
{
	public async Task<IActionResult> Login()
	{
		// Simulate login (hardcoding a username and role for simplicity)
		var claims = new List<Claim>
		{
			new(ClaimTypes.Name, "testuser"),
			new(ClaimTypes.Role, "Admin")
		};
		var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
		var principal = new ClaimsPrincipal(identity);

		// Thread.CurrentPrincipal is AsyncLocal-backed, so it rides on ExecutionContext
		Thread.CurrentPrincipal = principal;

		LogAmbientState("Before await");

		// Sign in the user
		await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal).ConfigureAwait(ConfigureAwaitOptions.ForceYielding | ConfigureAwaitOptions.None);

		LogAmbientState("After await");

		Task suppressedFlowTask;
		using (ExecutionContext.SuppressFlow())
		{
			suppressedFlowTask = Task.Run(() => LogAmbientState("Inside Task.Run with ExecutionContext suppressed"));
		}

		await suppressedFlowTask;

		return RedirectToAction("Index", "Home");

		void LogAmbientState(string stage) => logger.LogInformation(
			"{Stage} | Thread {ThreadId} | Thread.CurrentPrincipal: {CurrentPrincipal} | IHttpContextAccessor.HttpContext: {AccessorHttpContext} | Controller.HttpContext: {ControllerHttpContext} | principal local: {PrincipalLocal}",
			stage,
			Environment.CurrentManagedThreadId,
			Thread.CurrentPrincipal?.Identity?.Name ?? "<null>",
			httpContextAccessor.HttpContext is null ? "<null>" : "available",
			HttpContext is null ? "<null>" : "available",
			principal.Identity?.Name);
	}

	public async Task<IActionResult> Logout()
	{
		var user = User;

		// Sign out the user
		await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme).ConfigureAwait(ConfigureAwaitOptions.ForceYielding | ConfigureAwaitOptions.None);
		return RedirectToAction("Index", "Home");
	}
}