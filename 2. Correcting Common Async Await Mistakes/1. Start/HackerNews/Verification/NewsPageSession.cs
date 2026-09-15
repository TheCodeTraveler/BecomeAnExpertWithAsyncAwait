using HackerNews.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;

namespace HackerNews;

// Workshop plumbing: renders your News page in memory with Blazor's HtmlRenderer, the way Blazor renders it for a browser tab.
// The renderer runs component code one piece of work at a time, just like the renderer behind every browser tab of a Blazor Server app.
// Every session has its own services and its own FakeHackerNewsAPI, so it never touches the app's singletons or the real Hacker News.
public sealed class NewsPageSession : IAsyncDisposable
{
	// Every wait on your page gives up after this long, so a page that never answers fails its check instead of hanging the step
	public static readonly TimeSpan RenderTimeout = TimeSpan.FromSeconds(10);

	readonly ServiceProvider _serviceProvider;
	readonly HtmlRenderer _renderer;
	readonly NewsPageActivator _activator = new();

	HtmlRootComponent? _rootComponent;
	bool _isClosed;

	public NewsPageSession(FakeHackerNewsAPI hackerNewsApi)
	{
		HackerNewsApi = hackerNewsApi;

		var services = new ServiceCollection();
		services.AddLogging();
		services.AddSingleton<ILogger<NewsPageBase>>(Logger);
		services.AddSingleton<IHackerNewsAPI>(hackerNewsApi);
		services.AddSingleton<HackerNewsAPIService>();
		services.AddSingleton<IComponentActivator>(_activator);

		_serviceProvider = services.BuildServiceProvider();
		_renderer = new HtmlRenderer(_serviceProvider, _serviceProvider.GetRequiredService<ILoggerFactory>());
	}

	public FakeHackerNewsAPI HackerNewsApi { get; }

	public CapturingLogger Logger { get; } = new();

	public Dispatcher Dispatcher => _renderer.Dispatcher;

	// Completes when Blazor considers the page initialized: when the Task returned by its lifecycle methods completes
	public Task InitializationTask => RootComponent.QuiescenceTask;

	HtmlRootComponent RootComponent => _rootComponent ?? throw new InvalidOperationException("The News page has not been rendered yet");

	NewsPageProbe Page => _activator.Page ?? throw new InvalidOperationException("The News page has not been rendered yet");

	// Steps that render your page stop here while it still has an async void method: an exception that escapes one
	// can end the whole app, and a step must never do that
	public static bool HasAsyncVoidMethods(StepReport report)
	{
		var asyncVoidMethods = CodeInspector.GetAsyncVoidMethods(typeof(NewsPageBase));

		if (asyncVoidMethods.Count is 0)
			return false;

		report.Expect(
			"NewsPageBase has no async void methods",
			"none",
			string.Join(", ", asyncVoidMethods),
			false,
			"This step renders your page, and it will not while an exception could escape an async void method and end the app. Step 1 explains why, so make it pass again first.");

		return true;
	}

	public static int CountStoriesOnPage(string html) => html.Split("<article class=\"story-card\"").Length - 1;

	public static string DescribeStories(int count) => count is 1 ? "1 story" : $"{count} stories";

	// Renders the page for the first time. OnInitialized() and OnInitializedAsync() run up to their first incomplete await before this returns.
	public async Task BeginRendering(CancellationToken token)
	{
		_rootComponent = await _renderer.Dispatcher.InvokeAsync(() => _renderer.BeginRenderingComponent<NewsPageProbe>()).WaitAsync(RenderTimeout, token).ConfigureAwait(false);
	}

	// Waits for Blazor to finish initializing the page. False when it did not finish in time.
	public async Task<bool> WaitForInitialization(CancellationToken token)
	{
		try
		{
			await InitializationTask.WaitAsync(RenderTimeout, token).ConfigureAwait(false);
			return true;
		}
		catch (TimeoutException)
		{
			return false;
		}
	}

	// The HTML of the page as it was last rendered. Changing component state without a render does not change it.
	public Task<string> GetHtml(CancellationToken token) => Read(_ => RootComponent.ToHtmlString(), token);

	// Reads the page's state on Blazor's renderer, where the page reads and writes it
	public Task<T> Read<T>(Func<NewsPageProbe, T> read, CancellationToken token) => _renderer.Dispatcher.InvokeAsync(() => read(Page)).WaitAsync(RenderTimeout, token);

	// Presses Refresh: the refresh starts on Blazor's renderer, and the returned Task completes when the refresh does
	public Task PressRefresh() => _renderer.Dispatcher.InvokeAsync(Page.Refresh);

	// Closes the page, the way Blazor does when the user navigates away or closes the tab: the renderer disposes every component on it
	public async Task ClosePage()
	{
		if (_isClosed)
			return;

		_isClosed = true;

		try
		{
			await _renderer.DisposeAsync().AsTask().WaitAsync(RenderTimeout).ConfigureAwait(false);
		}
		catch (TimeoutException)
		{
			// Blazor's renderer is still blocked by your page. Abandon it rather than hang the step.
		}
	}

	// Nothing fails on the fake Hacker News unless a step asks it to, so any error during a refresh came from the page itself
	public async Task ExpectNoErrors(StepReport report, CancellationToken token)
	{
		var errorMessage = await Read(static page => page.ErrorMessage, token).ConfigureAwait(false);
		var errors = Logger.Errors;

		foreach (var error in errors)
		{
			report.Log($"Your page logged an error: \"{error.Message}\" ({error.Exception?.GetType().Name ?? "no exception"})");
		}

		var loggedExceptions = string.Join(", ", errors.Select(static error => error.Exception?.GetType().Name ?? "an error").Distinct());

		var actual = (errors.Count, errorMessage) switch
		{
			(0, null) => "no errors",
			(0, _) => "an error message on the page",
			(_, null) => $"logged {loggedExceptions}",
			_ => $"logged {loggedExceptions}, and an error message on the page",
		};

		report.Expect(
			"A refresh against a healthy Hacker News logs no errors and shows no error message",
			"no errors",
			actual,
			errors.Count is 0 && errorMessage is null,
			errors.Any(static error => error.Exception is InvalidOperationException)
				? "Your page logged an InvalidOperationException. Blazor throws it when a component renders off its renderer, which is where the code after ConfigureAwait(false) runs. Make those state changes, and StateHasChanged(), through InvokeAsync(...)."
				: "Nothing failed on the Hacker News side, so the error came from your page. The step's log lists every message your page logged, with its exception type. Compare it with the catch blocks in RefreshAsync(CancellationToken).");
	}

	public async ValueTask DisposeAsync()
	{
		await ClosePage().ConfigureAwait(false);
		await _serviceProvider.DisposeAsync().ConfigureAwait(false);
	}

	// Blazor asks this for every component it creates, which is how the session gets hold of the page it rendered
	sealed class NewsPageActivator : IComponentActivator
	{
		public NewsPageProbe? Page { get; private set; }

		public IComponent CreateInstance(Type componentType)
		{
			if (Activator.CreateInstance(componentType) is not IComponent component)
				throw new ArgumentException($"{componentType.Name} is not a component", nameof(componentType));

			if (component is NewsPageProbe page)
				Page = page;

			return component;
		}
	}
}