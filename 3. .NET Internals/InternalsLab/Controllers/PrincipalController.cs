using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InternalsLab.Controllers;

// Step 3: Principal
// The Run the experiment button on the Step 3 page leaves the Blazor app and loads /Principal/RunExperiment, a normal ASP.NET Core request.
// Every Observe(string) call is one checkpoint on the Step 3 page. Predict all four columns of every checkpoint before you run it.
public class PrincipalController(IHttpContextAccessor httpContextAccessor, LabNotebook notebook) : Controller
{
	// Step 3: [Authorize] sends you to the sign-in page first if the request has no sign-in cookie
	[Authorize]
	public async Task<IActionResult> RunExperiment()
	{
		var checkpoints = new List<Checkpoint>();

		// Step 3: an ordinary local variable that holds the signed-in user
		var signedInUser = HttpContext.User;

		// Step 3: checkpoint 1. Nothing in this action has awaited yet
		checkpoints.Add(Observe("1. Start of the action"));

		// Try it: delete this line, apply the change with Hot Reload, and click Run it again. Which cells change at checkpoints 2 and 3?
		Thread.CurrentPrincipal = signedInUser;

		// Yields the current thread: the rest of this method runs later as a continuation on the thread pool
		// Try it: replace this line with await Task.Delay(1).ConfigureAwait(false) and apply it with Hot Reload. Does httpContextAccessor still find the HttpContext at checkpoint 2?
		await Task.Yield();

		// Step 3: checkpoint 2. The continuation, usually on a different thread
		checkpoints.Add(Observe("2. After await Task.Yield()"));

		// Step 3: checkpoint 3. A thread pool thread running the Task.Run(...) lambda
		checkpoints.Add(await Task.Run(() => Observe("3. Inside Task.Run(...)")));

		// Step 3: checkpoint 4. The task is created while ExecutionContext flow is suppressed, and awaited only after the using block ends
		Task<Checkpoint> suppressedFlowTask;
		using (ExecutionContext.SuppressFlow())
		{
			suppressedFlowTask = Task.Run(() => Observe("4. Inside Task.Run(...) started while ExecutionContext flow is suppressed"));
		}

		checkpoints.Add(await suppressedFlowTask);

		// Workshop plumbing: saves the checkpoints in the lab notebook, then goes back to the Step 3 page to compare them with your predictions
		notebook.RecordPrincipalRun(checkpoints);

		return Redirect("/steps/3");

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