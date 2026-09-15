using Microsoft.AspNetCore.Components;

namespace CoffeeShop.Components.Pages;

public partial class HomePageBase : ComponentBase, IAsyncDisposable
{
	// A check that stops at the first stub finishes in about a millisecond, faster than anyone can see a spinner
	static readonly TimeSpan _minimumActivityIndicatorTime = TimeSpan.FromMilliseconds(600);

	readonly CancellationTokenSource _disposeCancellationTokenSource = new();

	Task _polling = Task.CompletedTask;
	bool _isStartingCheck;

	[Inject]
	public required StepVerifier Verifier { get; init; }

	[Inject]
	public required ILogger<HomePageBase> Logger { get; init; }

	public string? ErrorMessage { get; private set; }

	// The full exception behind ErrorMessage, when there is one
	public string? ErrorDetails { get; private set; }

	public int PassedStepCount => Verifier.Steps.Count(step => Verifier.GetState(step.Number).Status is StepStatus.Passed);

	public bool IsCheckingSteps => _isStartingCheck || Verifier.IsCheckingAllSteps;

	// The last step the check reaches: the first one that did not pass, or the last step checked on startup when they all passed
	public WorkshopStep LastCheckedStep => StoppedAtStep ?? Verifier.Steps.Last(static step => step.VerifyOnStartup);

	public WorkshopStep? StoppedAtStep => Verifier.Steps.FirstOrDefault(step => step.VerifyOnStartup && Verifier.GetState(step.Number).Status is not StepStatus.Passed);

	public async ValueTask DisposeAsync()
	{
		await _disposeCancellationTokenSource.CancelAsync().ConfigureAwait(false);

		await _polling.ConfigureAwait(false);

		_disposeCancellationTokenSource.Dispose();

		GC.SuppressFinalize(this);
	}

	protected override void OnInitialized()
	{
		_polling = PollVerifier(_disposeCancellationTokenSource.Token);
	}

	protected async Task CheckAllStepsAsync()
	{
		var token = _disposeCancellationTokenSource.Token;
		string? errorMessage = null;
		string? errorDetails = null;

		ErrorMessage = null;
		ErrorDetails = null;
		_isStartingCheck = true;

		// Show the activity indicator before the check starts
		await InvokeAsync(StateHasChanged).ConfigureAwait(false);

		try
		{
			// Keep the indicator on screen long enough to notice, even when Step 1 fails immediately
			await Task.WhenAll(Verifier.CheckAllSteps(), Task.Delay(_minimumActivityIndicatorTime, token)).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (token.IsCancellationRequested)
		{
			// The page was closed while the steps were being checked
			return;
		}
		catch (Exception e)
		{
			Logger.LogError(e, "Checking every step failed");

			errorMessage = "Checking the steps failed:";
			errorDetails = e.ToString();
		}

		// The continuation is off Blazor's renderer, so every component state change goes back through it
		await InvokeAsync(() =>
		{
			ErrorMessage = errorMessage;
			ErrorDetails = errorDetails;
			_isStartingCheck = false;

			StateHasChanged();
		}).ConfigureAwait(false);
	}

	// Re-renders while the verifier is busy, plus once more when it finishes, so results appear without a refresh
	async Task PollVerifier(CancellationToken token)
	{
		using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
		var wasBusy = true;

		try
		{
			while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
			{
				var isBusy = Verifier.IsBusy;

				if (isBusy || wasBusy)
					await InvokeAsync(StateHasChanged).ConfigureAwait(false);

				wasBusy = isBusy;
			}
		}
		catch (OperationCanceledException) when (token.IsCancellationRequested)
		{
			// Expected when the page is disposed
		}
	}
}