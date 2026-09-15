using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace InternalsLab.Components.Pages;

public partial class StepPageBase : ComponentBase, IAsyncDisposable
{
	// An experiment can finish faster than anyone can see a spinner
	static readonly TimeSpan _minimumActivityIndicatorTime = TimeSpan.FromMilliseconds(600);

	readonly CancellationTokenSource _disposeCancellationTokenSource = new();

	// The id of Step 2's Try it panel, which step text links to with [label](#try-it)
	public static string TryItAnchor => "try-it";

	// The line of Experiments/ExecutionContextExperiment.cs that the Try it panel links to
	public static string AwaitInsideSuppressFlowMarker => "Task AwaitInsideSuppressFlowAsync";

	[Parameter]
	public int Number { get; set; }

	[Inject]
	public required LabNotebook Notebook { get; init; }

	[Inject]
	public required NavigationManager NavigationManager { get; init; }

	[Inject]
	public required AuthenticationStateProvider AuthenticationStateProvider { get; init; }

	[Inject]
	public required ILogger<StepPageBase> Logger { get; init; }

	public WorkshopStep? Step { get; private set; }

	public string? SignedInUserName { get; private set; }

	public string? ErrorMessage { get; private set; }

	// The full exception behind ErrorMessage, when the experiment threw one you did not expect
	public string? ErrorDetails { get; private set; }

	public bool IsRunning { get; private set; }

	public bool IsTryingIt { get; private set; }

	public WorkshopStep? NextStep => Notebook.FindStep(Number + 1);

	// Kept in the lab notebook, so every step page opens code links the same way
	public CodeEditor CodeEditor
	{
		get => Notebook.CodeEditor;
		set => Notebook.CodeEditor = value;
	}

	// The panels that step text can link to right now. Step 2's Try it panel only appears once its experiment has run.
	public IReadOnlyCollection<string> Anchors => Step is Step2ExecutionContext && Notebook.GetProgress(Number).LatestRun is not null ? [TryItAnchor] : [];

	// Why Run the experiment is disabled, or null when it is not
	public string? RunBlockedReason
	{
		get
		{
			if (Step is null || IsRunning)
				return null;

			if (!Notebook.IsUnlocked(Number))
				return $"Finish Step {Number - 1} first.";

			var progress = Notebook.GetProgress(Number);
			var predictionCount = Step.CountPredictions(progress.Predictions);
			var requiredPredictionCount = Step.Checkpoints.Count * Step.Columns.Count;

			if (predictionCount < requiredPredictionCount)
				return $"Choose a prediction for every checkpoint first: {predictionCount} of {requiredPredictionCount} chosen. They do not have to be right.";

			if (Step.ExperimentUrl is not null && SignedInUserName is null)
				return "Sign in first. The experiment reads the signed-in user.";

			return null;
		}
	}

	public bool CanRun => Step is not null && !IsRunning && RunBlockedReason is null;

	public bool CanPredict => Notebook.IsUnlocked(Number) && Notebook.GetProgress(Number).LatestRun is null;

	public async ValueTask DisposeAsync()
	{
		// Stops waiting for an experiment that is still running. The experiment itself finishes on its own.
		await _disposeCancellationTokenSource.CancelAsync().ConfigureAwait(false);

		_disposeCancellationTokenSource.Dispose();

		GC.SuppressFinalize(this);
	}

	// Lets a long expression such as httpContextAccessor.HttpContext?.User wrap after each dot in a narrow table header
	protected static MarkupString BreakAfterDots(string code) => new MarkupString(WebUtility.HtmlEncode(code).Replace(".", ".<wbr />", StringComparison.Ordinal));

	// Blazor reuses this page when navigating from one step to another, so reset per-step state whenever Number changes
	protected override async Task OnParametersSetAsync()
	{
		if (Step?.Number == Number)
			return;

		Step = Notebook.FindStep(Number);
		ErrorMessage = null;
		ErrorDetails = null;

		if (Step?.ExperimentUrl is not null)
		{
			// The circuit's user comes from the sign-in cookie on the request that loaded this page
			var authenticationState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
			SignedInUserName = authenticationState.User.Identity?.IsAuthenticated is true ? authenticationState.User.Identity.Name : null;
		}
	}

	protected void Predict(int checkpoint, string columnId, ChangeEventArgs e) => Notebook.Predict(Number, checkpoint, columnId, e.Value?.ToString() ?? string.Empty);

	protected void Answer(string questionId, string answerId) => Notebook.Answer(Number, questionId, answerId);

	protected async Task RunExperimentAsync()
	{
		if (Step is null || !CanRun)
			return;

		// Step 3's experiment is an MVC request, so leave this Blazor circuit with a full page load. PrincipalController comes back to this page.
		if (Step.ExperimentUrl is { } experimentUrl)
		{
			NavigationManager.NavigateTo(experimentUrl, forceLoad: true);
			return;
		}

		var token = _disposeCancellationTokenSource.Token;
		var step = Step;
		string? errorMessage = null;
		string? errorDetails = null;

		ErrorMessage = null;
		ErrorDetails = null;
		IsRunning = true;

		var minimumActivityIndicatorTask = Task.Delay(_minimumActivityIndicatorTime, token);

		try
		{
			// Nothing above has awaited yet, so this still runs on Blazor's renderer, where every click handler starts.
			// Step 4's experiment depends on that.
			var results = await step.RunAsync(InvokeAsync, token).ConfigureAwait(false);

			Notebook.RecordRun(step.Number, results);

			await minimumActivityIndicatorTask.ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (token.IsCancellationRequested)
		{
			// The page was closed while the experiment was running
			return;
		}
		catch (TimeoutException e)
		{
			Logger.LogWarning(e, "Step {StepNumber}'s experiment did not finish within {Timeout}", step.Number, WorkshopStep.Timeout);

			errorMessage = $"The experiment did not finish within {WorkshopStep.Timeout.TotalSeconds} seconds. If you changed {step.ExperimentFile}, undo the change and run the app again.";
		}
		catch (Exception e)
		{
			Logger.LogError(e, "Step {StepNumber}'s experiment threw an unexpected exception", step.Number);

			errorMessage = "The experiment threw an unexpected exception:";
			errorDetails = SourceCode.WithRelativePaths(e.ToString());
		}

		// The continuation is off Blazor's renderer, so every component state change goes back through it
		await InvokeAsync(() =>
		{
			ErrorMessage = errorMessage;
			ErrorDetails = errorDetails;
			IsRunning = false;

			StateHasChanged();
		}).ConfigureAwait(false);
	}

	protected async Task TryItAsync()
	{
		if (Step is not Step2ExecutionContext step || IsTryingIt)
			return;

		var token = _disposeCancellationTokenSource.Token;
		string? errorMessage = null;
		string? errorDetails = null;

		ErrorMessage = null;
		ErrorDetails = null;
		IsTryingIt = true;

		var minimumActivityIndicatorTask = Task.Delay(_minimumActivityIndicatorTime, token);

		try
		{
			var outcome = await step.TryAwaitingInsideTheUsingBlock(Logger, token).ConfigureAwait(false);

			Notebook.RecordTryIt(step.Number, outcome);

			await minimumActivityIndicatorTask.ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (token.IsCancellationRequested)
		{
			// The page was closed while Try it was running
			return;
		}
		catch (Exception e)
		{
			Logger.LogError(e, "Try it on Step {StepNumber} threw an unexpected exception", step.Number);

			errorMessage = "Try it threw an unexpected exception:";
			errorDetails = SourceCode.WithRelativePaths(e.ToString());
		}

		await InvokeAsync(() =>
		{
			ErrorMessage = errorMessage;
			ErrorDetails = errorDetails;
			IsTryingIt = false;

			StateHasChanged();
		}).ConfigureAwait(false);
	}
}