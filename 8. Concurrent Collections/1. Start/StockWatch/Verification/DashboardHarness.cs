using System.Diagnostics;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using StockWatch.Components.Pages;

namespace StockWatch;

// Workshop plumbing: renders a fresh Dashboard with its own MarketDataService, the way a new browser tab does,
// and reaches the private members the steps name through reflection. They are private on purpose, so keep their names.
public sealed class DashboardHarness : IAsyncDisposable
{
	public const int SymbolCount = 60;
	public const string Finished = "finished";

	const BindingFlags _instanceMembers = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

	const string _semaphoreHint = "If you added a semaphore, check that every WaitAsync has a Release in a finally block, and that no method waits on the semaphore while it already holds it.";

	// Loading fetches 60 quotes that each take under 60 milliseconds, so these only run out when something is stuck
	static readonly TimeSpan _loadTimeout = TimeSpan.FromSeconds(5);
	static readonly TimeSpan _callTimeout = TimeSpan.FromSeconds(2);

	readonly ServiceProvider _serviceProvider;
	readonly HtmlRenderer _renderer;
	readonly Task _loaded;

	bool _isDisposed;
	bool _isLeftRunning;

	DashboardHarness(ServiceProvider serviceProvider, HtmlRenderer renderer, DashboardProbe dashboard, Task loaded)
	{
		_serviceProvider = serviceProvider;
		_renderer = renderer;
		_loaded = loaded;
		Dashboard = dashboard;
	}

	public DashboardProbe Dashboard { get; }

	// The checks call private members by name, so a renamed one is reported instead of throwing
	public static IReadOnlyList<string> FindMissingMembers(params string[] names) =>
		[.. names.Where(static name => typeof(DashboardPageBase).GetMember(name, _instanceMembers).Length is 0)];

	public static IReadOnlyList<FieldInfo> FindSemaphoreFields() =>
		[.. typeof(DashboardPageBase).GetFields(_instanceMembers).Where(static field => field.FieldType == typeof(SemaphoreSlim))];

	public static Type? FindFieldType(string name) => typeof(DashboardPageBase).GetField(name, _instanceMembers)?.FieldType;

	// Records a failed expected result for every member that is missing, and returns false when there is one
	public static bool HasMembers(StepReport report, params string[] names)
	{
		var missingMembers = FindMissingMembers(names);

		if (missingMembers.Count is 0)
			return true;

		report.Expect(
			"Dashboard.razor.cs still has every member this step calls",
			string.Join(", ", names),
			$"missing: {string.Join(", ", missingMembers)}",
			false,
			"The checks call these members by name. Keep their names and change what they do, the way the ToDo Refactor comments ask.");

		return false;
	}

	// Renders the page the way Blazor Server does for a new browser tab, which starts OnInitializedAsync()
	public static async Task<DashboardHarness> Render()
	{
		var services = new ServiceCollection();
		services.AddLogging();
		services.AddSingleton(new MarketDataService());

		var serviceProvider = services.BuildServiceProvider();
		var renderer = new HtmlRenderer(serviceProvider, serviceProvider.GetRequiredService<ILoggerFactory>());

		DashboardProbe? dashboard = null;

		var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
		{
			[nameof(DashboardProbe.Initialized)] = (Action<DashboardProbe>)(probe => dashboard = probe),
		});

		// Components run on the renderer's dispatcher, the same way they run on a circuit's in Blazor Server
		var root = await renderer.Dispatcher.InvokeAsync(() => renderer.BeginRenderingComponent<DashboardProbe>(parameters)).ConfigureAwait(false);

		return new DashboardHarness(serviceProvider, renderer, dashboard ?? throw new InvalidOperationException("The Dashboard page was not initialized"), root.QuiescenceTask);
	}

	// Describes how a call ended: "finished", "threw ObjectDisposedException", or "still running after 2 seconds"
	public static async Task<string> Describe(Task call, TimeSpan timeout, CancellationToken token)
	{
		try
		{
			await call.WaitAsync(timeout, token).ConfigureAwait(false);
			return Finished;
		}
		catch (TimeoutException)
		{
			return timeout.TotalSeconds is 1 ? "still running after 1 second" : $"still running after {timeout.TotalSeconds:0.#} seconds";
		}
		catch (Exception e) when (!token.IsCancellationRequested)
		{
			// Only the exception type goes to the page
			return $"threw {e.GetType().Name}";
		}
	}

	// Waits for OnInitializedAsync() to finish, and records a failed expected result when it does not
	public async Task<bool> Load(StepReport report, CancellationToken token)
	{
		var stopwatch = Stopwatch.StartNew();
		var outcome = await Describe(_loaded, _loadTimeout, token).ConfigureAwait(false);

		if (outcome is Finished)
		{
			report.Log($"A fresh dashboard loaded in {stopwatch.ElapsedMilliseconds} ms. OnInitializedAsync() applied the first round of quotes and started the refresh timer, and quotes applied reads {Dashboard.RefreshCount}");
			return true;
		}

		// Something in the timer code is stuck or broken, so disposing the page could hang, or dispose what a running timer still uses
		LeaveRunning();

		report.Log($"OnInitializedAsync() {outcome}");
		report.Expect(
			"A fresh dashboard finishes loading",
			"finished within 5 seconds",
			$"OnInitializedAsync() {outcome}",
			false,
			$"OnInitializedAsync() did not finish, so this page would never start refreshing. It awaits RefreshQuotes() and then StartRefreshTimer(). {_semaphoreHint}");

		return false;
	}

	// Steps 1 to 3 stop the timer once the page has loaded, so a tick 2 seconds in can never change what they measure
	public async Task<bool> LoadAndStopTimer(StepReport report, CancellationToken token)
	{
		if (!await Load(report, token).ConfigureAwait(false))
			return false;

		var outcome = await Describe(StopRefreshTimer(), _callTimeout, token).ConfigureAwait(false);

		if (outcome is Finished)
		{
			report.Log("Stopped the refresh timer, so no timer tick changes the results below");
			return true;
		}

		LeaveRunning();

		report.Expect("StopRefreshTimer() stops the refresh timer", Finished, outcome, false, $"StopRefreshTimer() did not finish. {_semaphoreHint}");

		return false;
	}

	public object? GetField(string name) => typeof(DashboardPageBase).GetField(name, _instanceMembers)?.GetValue(Dashboard);

	public IReadOnlyList<SemaphoreSlim> GetSemaphores() => [.. FindSemaphoreFields().Select(field => field.GetValue(Dashboard)).OfType<SemaphoreSlim>()];

	public Task RefreshQuotes(CancellationToken token) => Invoke("RefreshQuotes", [typeof(CancellationToken)], [token]);

	public Task StartRefreshTimer() => Invoke("StartRefreshTimer", Type.EmptyTypes, []);

	public Task StopRefreshTimer() => Invoke("StopRefreshTimer", Type.EmptyTypes, []);

	// Keeps the page from being disposed. A check calls this when a refresh timer might still be running with nothing to stop it:
	// DisposeAsync() disposes the CancellationTokenSource that the timer's callback reads, and an exception in that async void callback would end the app.
	// Cancelling that source without disposing it is safe, and it turns every later tick of a leaked timer into a refresh that stops before it fetches anything.
	public void LeaveRunning()
	{
		_isLeftRunning = true;

		try
		{
			if (GetField("_disposeCancellationTokenSource") is CancellationTokenSource disposeCancellationTokenSource)
				disposeCancellationTokenSource.Cancel();
		}
		catch (ObjectDisposedException)
		{
			// The page already disposed it, so there is nothing left to cancel
		}
	}

	// Disposes the page the way Blazor Server does when a browser tab closes, which calls its DisposeAsync()
	public async Task<string> DisposeDashboard(CancellationToken token)
	{
		if (_isLeftRunning)
			return "left running";

		if (_isDisposed)
			return Finished;

		_isDisposed = true;

		var outcome = await Describe(DisposeRenderer(), _callTimeout, token).ConfigureAwait(false);

		if (outcome is Finished)
			await _serviceProvider.DisposeAsync().ConfigureAwait(false);

		return outcome;
	}

	public async ValueTask DisposeAsync()
	{
		await DisposeDashboard(CancellationToken.None).ConfigureAwait(false);
	}

	async Task DisposeRenderer() => await _renderer.DisposeAsync().ConfigureAwait(false);

	Task Invoke(string name, Type[] parameterTypes, object?[] arguments)
	{
		try
		{
			var method = typeof(DashboardPageBase).GetMethod(name, _instanceMembers, parameterTypes) ?? throw new MissingMethodException(nameof(DashboardPageBase), name);

			return method.Invoke(Dashboard, BindingFlags.DoNotWrapExceptions, null, arguments, null) switch
			{
				Task task => task,
				ValueTask valueTask => valueTask.AsTask(),
				_ => Task.CompletedTask,
			};
		}
		catch (Exception e)
		{
			return Task.FromException(e);
		}
	}
}