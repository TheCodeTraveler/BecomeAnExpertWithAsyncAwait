using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddHttpContextAccessor();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
	.AddCookie(static options => options.LoginPath = "/");

var app = builder.Build();

// Reads the sign-in cookie on every request and assigns the signed-in user to HttpContext.User
app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultControllerRoute();

app.Run();