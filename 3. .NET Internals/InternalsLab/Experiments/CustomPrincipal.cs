using System.Security.Principal;

namespace InternalsLab;

// Step 2: the background thread's principal, so its values are easy to tell apart from the main thread's ClaimsPrincipal
sealed class CustomPrincipal() : GenericPrincipal(new CustomIdentity(), null)
{
	sealed class CustomIdentity : IIdentity
	{
		public string AuthenticationType => "Fake";
		public bool IsAuthenticated => true;
		public string Name => nameof(CustomIdentity);
	}
}