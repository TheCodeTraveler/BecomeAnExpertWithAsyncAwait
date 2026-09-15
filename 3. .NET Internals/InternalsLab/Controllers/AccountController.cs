using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace InternalsLab.Controllers;

public class AccountController : Controller
{
	// The Blazor pages cannot render an antiforgery token for a form that posts to MVC, so signing in happens on this small MVC view
	public IActionResult SignIn() => View();

	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Login(string? name)
	{
		if (!string.IsNullOrWhiteSpace(name))
		{
			var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, name.Trim())], CookieAuthenticationDefaults.AuthenticationScheme);

			// Writes the sign-in cookie. HttpContext.User is assigned from that cookie on the next request.
			await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
		}

		return Redirect("/steps/3");
	}

	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Logout()
	{
		await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

		return RedirectToAction(nameof(SignIn));
	}
}