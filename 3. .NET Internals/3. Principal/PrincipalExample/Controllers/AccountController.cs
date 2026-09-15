using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace PrincipalExample.Controllers;

public class AccountController : Controller
{
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

		return RedirectToAction(nameof(HomeController.Index), "Home");
	}

	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Logout()
	{
		await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

		return RedirectToAction(nameof(HomeController.Index), "Home");
	}
}