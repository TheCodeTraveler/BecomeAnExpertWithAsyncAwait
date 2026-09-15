namespace PrincipalExample.Models;

public record Checkpoint(
	string Name,
	int ThreadId,
	string? ThreadCurrentPrincipal,
	string? HttpContextAccessorUser,
	string? ControllerHttpContextUser,
	string? SignedInUserVariable);