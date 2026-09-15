using System.Diagnostics;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using ProductDetails.Components.Pages;

namespace ProductDetails;

// Workshop plumbing: renders your Product page in the background, the way a browser tab would, with fresh copies of the five services.
// It times every load, records every paint, and watches for anything that holds up Blazor's renderer while the page waits.
public sealed class ProductPageSession : IAsyncDisposable
{
	// What the five services cost, from BackendServices.cs: 4.2 seconds one after another, and 1.2 seconds, the slowest one, side by side
	public const double SlowestServiceSeconds = 1.2;
	public const double AllServicesSeconds = 4.2;

	// A load that answers faster than the slowest service did not wait for it. One that takes longer than 1.6 seconds still waits for some services one after another.
	public const double ShortestPageLoadSeconds = 1.1;
	public const double LongestPageLoadSeconds = 1.6;

	// Each service's cost, in the order the page shows the cards
	public static readonly IReadOnlyList<(string Name, double Seconds)> ServiceLatencies =
	[
		("Inventory", 0.7),
		("Pricing", 0.9),
		("Reviews", 1.2),
		("Shipping", 0.6),
		("Recommendations", 0.8),
	];

	// The starter takes 4.2 seconds, so a load only runs out of time when something waits forever
	public static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(8);

	// A load paints about ten times. A page stuck in a loop that repaints can paint millions of times, so stop recording long before that uses up memory.
	const int _maximumRecordedPaints = 1_000;

	static readonly TimeSpan _heartbeatInterval = TimeSpan.FromMilliseconds(10);
	static readonly TimeSpan _disposeTimeout = TimeSpan.FromSeconds(2);

	readonly Lock _lock = new();
	readonly List<PagePaint> _paints = [];
	readonly ServiceProvider _serviceProvider;
	readonly HtmlRenderer _renderer;

	long _loadStartedAt;
	bool _isClosed;
	HtmlRootComponent _root;
	ProductPageProbe? _page;

	public ProductPageSession()
	{
		var services = new ServiceCollection();
		services.AddLogging();
		services.AddSingleton<ILogger<ProductPageBase>>(Logger);

		// Fresh instances, never the singletons the Product page uses
		services.AddSingleton(new InventoryService());
		services.AddSingleton(new PricingService());
		services.AddSingleton(new ReviewsService());
		services.AddSingleton(new ShippingService());
		services.AddSingleton(new RecommendationsService());

		_serviceProvider = services.BuildServiceProvider();
		_renderer = new HtmlRenderer(_serviceProvider, _serviceProvider.GetRequiredService<ILoggerFactory>());
	}

	// Everything the page logged while this session rendered it
	public PageLogRecorder Logger { get; } = new();

	public static string DescribeCards(IEnumerable<PanelState> panels) =>
		string.Join(", ", panels.Select(static panel => panel.Seconds is { } seconds ? $"{panel.Name} {panel.Status} at {seconds:F1}s" : $"{panel.Name} {panel.Status}"));

	public async ValueTask DisposeAsync()
	{
		// From here on, a page that is still repainting gets an exception instead of a paint, which ends a load that would otherwise loop forever
		lock (_lock)
		{
			_isClosed = true;
		}

		// A page that still holds the renderer can keep it from ever disposing, so stop waiting after a moment and leave it to the garbage collector
		try
		{
			await _renderer.DisposeAsync().AsTask().WaitAsync(_disposeTimeout).ConfigureAwait(false);
			await _serviceProvider.DisposeAsync().ConfigureAwait(false);
		}
		catch (TimeoutException)
		{
		}
	}

	// Opens the page, the way a browser tab does, which runs OnInitializedAsync() and loads the product
	public Task<PageLoad> OpenPageAsync(CancellationToken token) => MeasureLoadAsync(
		() =>
		{
			var parameters = ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(ProductPageProbe.Session)] = this });

			_root = _renderer.BeginRenderingComponent<ProductPageProbe>(parameters);

			// Completes when OnInitializedAsync() has finished and the page has painted its result
			return _root.QuiescenceTask;
		},
		token);

	// Does what clicking Load product page does, on the page OpenPageAsync() opened
	public Task<PageLoad> PressLoadProductPageAsync(CancellationToken token) => MeasureLoadAsync(
		() => _page?.PressLoadProductPageAsync() ?? throw new InvalidOperationException("Open the page before pressing Load product page"),
		token);

	public void Attach(ProductPageProbe page) => _page = page;

	public void RecordPaint(IReadOnlyList<PanelState> panels)
	{
		lock (_lock)
		{
			ObjectDisposedException.ThrowIf(_isClosed, this);

			if (_paints.Count < _maximumRecordedPaints)
				_paints.Add(new PagePaint(Stopwatch.GetElapsedTime(_loadStartedAt), [.. panels]));
		}
	}

	async Task<PageLoad> MeasureLoadAsync(Func<Task> load, CancellationToken token)
	{
		lock (_lock)
		{
			_paints.Clear();
			_loadStartedAt = Stopwatch.GetTimestamp();
		}

		var loadStartedAt = Stopwatch.GetTimestamp();

		using var heartbeatCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(token);
		var heartbeat = WatchRendererAsync(heartbeatCancellationTokenSource.Token);

		var finished = false;

		try
		{
			// The page's own code runs on Blazor's renderer, exactly as it does in the browser.
			// An idle renderer runs work on the thread that asks for it, so ask from a thread pool thread: a page that blocks forever must not take this step's thread with it.
			await Task.Run(() => _renderer.Dispatcher.InvokeAsync(load), token).WaitAsync(LoadTimeout, token).ConfigureAwait(false);

			finished = true;
		}
		catch (TimeoutException)
		{
			// Still loading. The step reports it.
		}
		finally
		{
			await heartbeatCancellationTokenSource.CancelAsync().ConfigureAwait(false);
		}

		var elapsed = Stopwatch.GetElapsedTime(loadStartedAt);
		var longestRendererPause = await heartbeat.ConfigureAwait(false);

		List<PagePaint> paints;

		lock (_lock)
		{
			paints = [.. _paints];
		}

		if (!finished || _page is not { } page)
			return new PageLoad(false, elapsed, longestRendererPause, paints, [], null, null, true, string.Empty);

		// The page's state is only safe to read on the renderer
		return await _renderer.Dispatcher.InvokeAsync(() => new PageLoad(
			true,
			elapsed,
			longestRendererPause,
			paints,
			[.. page.Panels],
			page.PageError,
			page.TotalSeconds,
			page.IsLoading,
			_root.ToHtmlString())).WaitAsync(LoadTimeout, token).ConfigureAwait(false);
	}

	// Queues an empty work item on Blazor's renderer every few milliseconds and measures how long each one waits to run.
	// An await gives the renderer back straight away. A blocking wait such as .Wait(), .Result or Task.WaitAll holds it until the tasks finish.
	async Task<TimeSpan> WatchRendererAsync(CancellationToken token)
	{
		var longestPause = TimeSpan.Zero;

		while (true)
		{
			var queuedAt = Stopwatch.GetTimestamp();

			try
			{
				await _renderer.Dispatcher.InvokeAsync(static () => { }).WaitAsync(token).ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (token.IsCancellationRequested)
			{
				// The load ended while this work item was still waiting, so it waited at least this long
				return Longest(longestPause, Stopwatch.GetElapsedTime(queuedAt));
			}

			longestPause = Longest(longestPause, Stopwatch.GetElapsedTime(queuedAt));

			try
			{
				await Task.Delay(_heartbeatInterval, token).ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (token.IsCancellationRequested)
			{
				return longestPause;
			}
		}

		static TimeSpan Longest(TimeSpan first, TimeSpan second) => first > second ? first : second;
	}
}