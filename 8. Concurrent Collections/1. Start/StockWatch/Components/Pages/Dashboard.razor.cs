using Microsoft.AspNetCore.Components;

namespace StockWatch.Components.Pages;

public partial class DashboardPageBase : ComponentBase, IAsyncDisposable
{
	// ToDo Refactor (Step 2): Dictionary is not thread safe. Every symbol is fetched in
	// parallel, so more than one write to this can be in flight at once.
	readonly Dictionary<string, StockQuoteModel> _latestQuotes = [];

	readonly CancellationTokenSource _disposeCancellationTokenSource = new();

	Timer? _refreshTimer;
	int _refreshCount;

	[Inject]
	public required MarketDataService MarketDataService { get; init; }

	// ToDo Refactor (Step 3): Blazor's renderer reads this on a different thread than the
	// one that last wrote _refreshCount, and a plain read can hand it a stale value.
	public int RefreshCount => _refreshCount;

	public IReadOnlyList<StockSymbolModel> Symbols => GetSymbols();

	// ToDo Refactor (Step 4): this stops the timer while StartRefreshTimer() may still be
	// running. Anything you add to guard the timer has to be cleaned up here too.
	public async ValueTask DisposeAsync()
	{
		await _disposeCancellationTokenSource.CancelAsync().ConfigureAwait(false);

		await StopRefreshTimer().ConfigureAwait(false);

		_disposeCancellationTokenSource.Dispose();

		GC.SuppressFinalize(this);
	}

	protected override async Task OnInitializedAsync()
	{
		await RefreshQuotes(_disposeCancellationTokenSource.Token).ConfigureAwait(false);

		await StartRefreshTimer().ConfigureAwait(false);
	}

	// ToDo Refactor (Step 4): two callers can both read _refreshTimer as null and both
	// create a timer. Nothing guards this field.
	async ValueTask StartRefreshTimer()
	{
		await StopRefreshTimer().ConfigureAwait(false);

		_refreshTimer = new Timer(async _ =>
		{
			using var refreshCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(_disposeCancellationTokenSource.Token);
			refreshCancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(5));

			await RefreshQuotes(refreshCancellationTokenSource.Token).ConfigureAwait(false);
		});

		_refreshTimer.Change(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
	}

	// ToDo Refactor (Step 4): this reads and writes _refreshTimer too, and nothing stops it
	// running at the same time as StartRefreshTimer(), which also calls it.
	async ValueTask StopRefreshTimer()
	{
		if (_refreshTimer is not null)
		{
			await _refreshTimer.DisposeAsync().ConfigureAwait(false);
			_refreshTimer = null;
		}
	}

	async Task RefreshQuotes(CancellationToken token)
	{
		try
		{
			// Parallel.ForEachAsync runs these iterations concurrently, so more
			// than one of the writes below can be in flight at once.
			await Parallel.ForEachAsync(
				MarketDataService.Symbols,
				token,
				async (symbol, cancellationToken) =>
				{
					var quote = await MarketDataService.GetStockQuote(symbol, cancellationToken).ConfigureAwait(false);

					// ToDo Refactor (Step 2): this is a read, then a write, on a collection
					// that many threads are touching. It can corrupt the Dictionary
					// or throw, and one update can overwrite a newer one.
					if (!_latestQuotes.TryAdd(symbol, quote))
					{
						_latestQuotes[symbol] = quote;
					}

					// ToDo Refactor (Step 3): `++` is a read, an add, and a write. Increments get lost.
					_refreshCount++;
				}).ConfigureAwait(false);

			// The continuation is off Blazor's renderer, so marshal the UI update back
			await InvokeAsync(StateHasChanged).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (token.IsCancellationRequested)
		{
			// Expected when the component is disposed or a refresh times out
		}
	}

	IReadOnlyList<StockSymbolModel> GetSymbols()
	{
		// ToDo Refactor (Step 1): List is not thread safe, and Parallel.ForEach can call
		// Add from more than one worker at once. Items go missing or this throws.
		List<StockSymbolModel> symbols = [];

		Parallel.ForEach(MarketDataService.Symbols, symbol =>
		{
			_latestQuotes.TryGetValue(symbol, out var quote);

			symbols.Add(new StockSymbolModel(symbol, MarketDataService.GetCompanyName(symbol), quote));
		});

		return [.. symbols.OrderBy(static symbol => symbol.Symbol)];
	}
}