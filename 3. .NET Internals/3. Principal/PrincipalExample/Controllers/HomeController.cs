using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PrincipalExample.Models;

namespace PrincipalExample.Controllers;

public class HomeController(IHttpContextAccessor httpContextAccessor) : Controller
{
	public IActionResult Index() => View();

	[Authorize]
	public async Task<IActionResult> RunExperiment()
	{
		var checkpoints = new List<Checkpoint>();
		var signedInUser = HttpContext.User;

		checkpoints.Add(Observe("1. Start of the action"));

		Thread.CurrentPrincipal = signedInUser;

		// Yields the current thread: the rest of this method runs later as a continuation on the thread pool
		await Task.Yield();

		checkpoints.Add(Observe("2. After await Task.Yield()"));

		checkpoints.Add(await Task.Run(() => Observe("3. Inside Task.Run(...)")));

		Task<Checkpoint> suppressedFlowTask;
		using (ExecutionContext.SuppressFlow())
		{
			suppressedFlowTask = Task.Run(() => Observe("4. Inside Task.Run(...) started while ExecutionContext flow is suppressed"));
		}

		checkpoints.Add(await suppressedFlowTask);

		return View(nameof(Index), checkpoints);

		// Asks for the signed-in user's name four different ways, from whichever thread is running this code
		Checkpoint Observe(string checkpoint) => new Checkpoint(
			checkpoint,
			Environment.CurrentManagedThreadId,
			Thread.CurrentPrincipal?.Identity?.Name,
			httpContextAccessor.HttpContext?.User.Identity?.Name,
			HttpContext.User.Identity?.Name,
			signedInUser.Identity?.Name);
	}
}