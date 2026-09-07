using Microsoft.AspNetCore.Components;

namespace StockWatch.Components.Pages;

public partial class DashboardPageBase : ComponentBase, IAsyncDisposable
{
	// ToDo Refactor: Dictionary is not thread safe. Every symbol is fetched in
	// parallel, so many threads write to this at the same time.
	readonly Dictionary<string, StockQuoteModel> _latestQuotes = [];

	readonly CancellationTokenSource _disposeCancellationTokenSource = new();

	Timer? _refreshTimer;
	int _refreshCount;

	[Inject]
	public required MarketDataService MarketDataService { get; init; }

	public int RefreshCount => _refreshCount;

	public IReadOnlyList<StockSymbolModel> Symbols => GetSymbols();

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

	// ToDo Refactor: two callers can both read _refreshTimer as null and both
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
			// Every symbol is fetched in parallel, so every write below
			// happens on a different Thread Pool thread at the same time.
			await Parallel.ForEachAsync(
				MarketDataService.Symbols,
				token,
				async (symbol, cancellationToken) =>
				{
					var quote = await MarketDataService.GetStockQuote(symbol, cancellationToken).ConfigureAwait(false);

					// ToDo Refactor: this is a read, then a write, on a collection
					// that many threads are touching. It can corrupt the Dictionary
					// or throw, and one update can overwrite a newer one.
					if (!_latestQuotes.TryAdd(symbol, quote))
					{
						_latestQuotes[symbol] = quote;
					}

					// ToDo Refactor: `++` is a read, an add, and a write. Increments get lost.
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
		// ToDo Refactor: List is not thread safe, and Parallel.ForEach calls
		// Add from many threads at once. Items go missing or this throws.
		List<StockSymbolModel> symbols = [];

		Parallel.ForEach(MarketDataService.Symbols, symbol =>
		{
			_latestQuotes.TryGetValue(symbol, out var quote);

			symbols.Add(new StockSymbolModel(symbol, MarketDataService.GetCompanyName(symbol), quote));
		});

		return [.. symbols.OrderBy(static symbol => symbol.Symbol)];
	}
}