namespace InternalsLab;

// This file holds the answers to Step 3. Run the experiment and answer the questions on the page before you read it.
public sealed class Step3Principal : WorkshopStep
{
	public const int StepNumber = 3;

	const string _threadCurrentPrincipalColumn = "thread-current-principal";
	const string _httpContextAccessorColumn = "http-context-accessor";
	const string _controllerHttpContextColumn = "controller-http-context";
	const string _signedInUserColumn = "signed-in-user";
	const string _nameValue = "name";
	const string _nullValue = "null";

	static readonly IReadOnlyList<PredictionChoice> _nameOrNull =
	[
		new PredictionChoice(_nameValue, "Your name"),
		new PredictionChoice(_nullValue, "null"),
	];

	public override int Number => StepNumber;

	public override string Scenario => "Principal";

	public override string Title => "Thread.CurrentPrincipal, IHttpContextAccessor, HttpContext.User";

	public override string Story => "Step 2 showed ExecutionContext flowing through a console app. This experiment shows the same mechanism inside an ASP.NET Core request, where the ambient values are the signed-in user and the current HttpContext. "
		+ "RunExperiment() in [Controllers/PrincipalController.cs](Controllers/PrincipalController.cs#Task<IActionResult> RunExperiment) records four checkpoints, and at each one it asks for the signed-in user's name four different ways. "
		+ "Some of those values are still there after an await because ExecutionContext carried them. Others are simply object references the code already holds. Predict every cell as your name or null.";

	public override int RecommendedMinutes => 10;

	public override string ExperimentFile => "Controllers/PrincipalController.cs";

	public override string? ExperimentUrl => "Principal/RunExperiment";

	public override IReadOnlyList<string> TryIt { get; } =
	[
		"Delete Thread.CurrentPrincipal = signedInUser; in [PrincipalController.cs](Controllers/PrincipalController.cs#Try it: delete this line) and apply the change with Hot Reload, then click Run it again. Which cells change at checkpoints 2 and 3?",
		"Replace await Task.Yield() in [PrincipalController.cs](Controllers/PrincipalController.cs#Try it: replace this line) with await Task.Delay(1).ConfigureAwait(false) and apply it with Hot Reload. Does httpContextAccessor still find the HttpContext at checkpoint 2?",
	];

	public override IReadOnlyList<ExperimentCheckpoint> Checkpoints { get; } =
	[
		new ExperimentCheckpoint(1, "1. Start of the action"),
		new ExperimentCheckpoint(2, "2. After await Task.Yield()"),
		new ExperimentCheckpoint(3, "3. Inside Task.Run(...)"),
		new ExperimentCheckpoint(4, "4. Inside Task.Run(...) started while ExecutionContext flow is suppressed"),
	];

	public override IReadOnlyList<PredictionColumn> Columns { get; } =
	[
		new PredictionColumn(_threadCurrentPrincipalColumn, "Thread.CurrentPrincipal", _nameOrNull),
		new PredictionColumn(_httpContextAccessorColumn, "httpContextAccessor.HttpContext?.User", _nameOrNull),
		new PredictionColumn(_controllerHttpContextColumn, "HttpContext.User", _nameOrNull),
		new PredictionColumn(_signedInUserColumn, "signedInUser", _nameOrNull),
	];

	public override IReadOnlyList<ExplainQuestion> Questions { get; } =
	[
		new ExplainQuestion("what-only-looks-like-it-flowed", "At checkpoint 4, why do HttpContext.User and signedInUser still show your name while Thread.CurrentPrincipal and httpContextAccessor.HttpContext?.User are null?",
		[
			new ExplainAnswer("a", "ASP.NET Core copies HttpContext.User onto every thread pool thread that runs code for the request.", false,
				"If it did, Thread.CurrentPrincipal would already hold your name at checkpoint 1. Ask which expressions read ambient state and which read objects the code already holds."),
			new ExplainAnswer("b", "Suppressing flow only affects static properties, and HttpContext.User and signedInUser are instance values.", false,
				"httpContextAccessor.HttpContext is an instance property too, and it came back null. What is behind that property?"),
			new ExplainAnswer("c", "HttpContext.User is read through the controller instance and signedInUser is a captured local variable, so both are ordinary object references. The other two are looked up in AsyncLocal<T> storage, which only ExecutionContext carries.", true,
				"Right. A value that is still there after an await has not necessarily flowed. It may be an object reference the compiler kept for you."),
		]),
		new ExplainQuestion("who-assigns-thread-current-principal", "Why is Thread.CurrentPrincipal null at checkpoint 1?",
		[
			new ExplainAnswer("a", "Nothing in ASP.NET Core assigns it. The authentication middleware assigns HttpContext.User, and Thread.CurrentPrincipal only holds a user if your own code puts it there.", true,
				"Right. In ASP.NET Core, HttpContext.User, also exposed as the controller's User property, is the principal to use."),
			new ExplainAnswer("b", "The sign-in cookie is not read until the action's first await.", false,
				"Three columns already show your name at checkpoint 1, so the cookie was read before the action ran. [Program.cs](Program.cs#Step 3: reads the sign-in cookie) shows what reads it."),
			new ExplainAnswer("c", "[Authorize] clears Thread.CurrentPrincipal before the action runs.", false,
				"[Authorize] only checks HttpContext.User. Look in [Program.cs](Program.cs#Step 3: reads the sign-in cookie) and [AccountController.cs](Controllers/AccountController.cs#Writes the sign-in cookie) for anything that assigns Thread.CurrentPrincipal."),
		]),
		new ExplainQuestion("run-to-run", "Run the experiment a few times. What changes from run to run, and why?",
		[
			new ExplainAnswer("a", "The names and nulls change, depending on which thread happens to run each checkpoint.", false,
				"Run it again and compare the two runs. The Thread column shows the previous run's thread IDs too. Did any name or null change?"),
			new ExplainAnswer("b", "Nothing changes, because each checkpoint always runs on the same thread.", false,
				"Run it again and compare the Thread column with the previous run's thread IDs."),
			new ExplainAnswer("c", "Only the thread IDs change. The names and nulls never do, because ExecutionContext belongs to the work item, not to the thread that happens to run it.", true,
				"Right. Read the columns, not the rows."),
		]),
	];

	public static IReadOnlyList<CheckpointResult> ToResults(IReadOnlyList<Checkpoint> checkpoints) =>
		[.. checkpoints.Select(static (checkpoint, index) => new CheckpointResult(
			index + 1,
			checkpoint.ThreadId,
			null,
			new Dictionary<string, ObservedValue>
			{
				[_threadCurrentPrincipalColumn] = Observe(checkpoint.ThreadCurrentPrincipal),
				[_httpContextAccessorColumn] = Observe(checkpoint.HttpContextAccessorUser),
				[_controllerHttpContextColumn] = Observe(checkpoint.ControllerHttpContextUser),
				[_signedInUserColumn] = Observe(checkpoint.SignedInUserVariable),
			},
			[]))];

	public override string GetHint(int checkpoint, string columnId) => (columnId, checkpoint) switch
	{
		(_threadCurrentPrincipalColumn, 1) => "Nothing in RunExperiment() has assigned Thread.CurrentPrincipal yet. Open [Program.cs](Program.cs#Step 3: reads the sign-in cookie) and [AccountController.cs](Controllers/AccountController.cs#Writes the sign-in cookie): does anything in ASP.NET Core assign it for you?",
		(_threadCurrentPrincipalColumn, 2) => "The action assigned Thread.CurrentPrincipal right before await Task.Yield(), in [PrincipalController.cs](Controllers/PrincipalController.cs#Thread.CurrentPrincipal = signedInUser;), and the continuation usually runs on a different thread. What does await capture and restore on the new thread?",
		(_threadCurrentPrincipalColumn, 3) => "Task.Run(...) captured ExecutionContext when the action created the task, after Thread.CurrentPrincipal was assigned.",
		(_threadCurrentPrincipalColumn, _) => "This task was created inside using (ExecutionContext.SuppressFlow()). Thread.CurrentPrincipal is stored in ExecutionContext, so ask what the task captured.",
		(_httpContextAccessorColumn, 1) => "The authentication middleware, added in [Program.cs](Program.cs#Step 3: reads the sign-in cookie), assigned HttpContext.User before the controller ran, and IHttpContextAccessor finds the HttpContext of the request that is running.",
		(_httpContextAccessorColumn, 2 or 3) => "IHttpContextAccessor.HttpContext is looked up in an AsyncLocal<T>, and the code is on a different thread now. What carries AsyncLocal<T> values to that thread?",
		(_httpContextAccessorColumn, _) => "The httpContextAccessor object is still reachable here, but its HttpContext property is an AsyncLocal<T> lookup. What did a task created with flow suppressed capture?",
		(_controllerHttpContextColumn, _) => "HttpContext is a property of the controller instance, which MVC assigned when it created the controller. Does reading a property of an object you hold depend on the thread, or on ExecutionContext?",
		_ => "signedInUser is a local variable that the Observe local function in [PrincipalController.cs](Controllers/PrincipalController.cs#Checkpoint Observe) uses, so the compiler keeps it in a closure object shared by the async state machine and both lambdas. Which threads can read it?",
	};

	// Step 3 runs in an MVC request, not in this Blazor circuit: the page loads ExperimentUrl, and PrincipalController records the results
	public override Task<IReadOnlyList<CheckpointResult>> RunAsync(Func<Action, Task> invokeAsync, CancellationToken token) =>
		throw new NotSupportedException($"Step {StepNumber} runs in an MVC request. Load {ExperimentUrl} instead.");

	static ObservedValue Observe(string? name) => name is null ? new ObservedValue(_nullValue, "null") : new ObservedValue(_nameValue, name);
}