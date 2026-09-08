using System.Collections.Concurrent;
using Microsoft.AspNetCore.Components;

namespace StockWatch.Components.Pages;

public partial class DashboardPageBase : ComponentBase, IAsyncDisposable
{
	// ConcurrentDictionary is safe for many writers at once. AddOrUpdate replaces
	// TryAdd followed by an indexer assignment with one thread-safe call, so no
	// update can be lost between two separate operations.
	readonly ConcurrentDictionary<string, StockQuoteModel> _latestQuotes = new();

	// SemaphoreSlim is the asynchronous lock guarding the timer field.
	// `lock` cannot be held across an await, and DisposeAsync is awaited.
	readonly SemaphoreSlim _timerSemaphore = new(1, 1);

	readonly CancellationTokenSource _disposeCancellationTokenSource = new();

	Timer? _refreshTimer;
	int _refreshCount;

	[Inject]
	public required MarketDataService MarketDataService { get; init; }

	public int RefreshCount => Volatile.Read(ref _refreshCount);

	public IReadOnlyList<StockSymbolModel> Symbols => GetSymbols();

	public async ValueTask DisposeAsync()
	{
		await _disposeCancellationTokenSource.CancelAsync().ConfigureAwait(false);

		await StopRefreshTimer().ConfigureAwait(false);

		_disposeCancellationTokenSource.Dispose();
		_timerSemaphore.Dispose();

		GC.SuppressFinalize(this);
	}

	protected override async Task OnInitializedAsync()
	{
		await RefreshQuotes(_disposeCancellationTokenSource.Token).ConfigureAwait(false);

		await StartRefreshTimer().ConfigureAwait(false);
	}

	async ValueTask StartRefreshTimer()
	{
		await _timerSemaphore.WaitAsync(_disposeCancellationTokenSource.Token).ConfigureAwait(false);

		try
		{
			// Call the unguarded version: SemaphoreSlim is not reentrant, so
			// calling StopRefreshTimer() here would deadlock against this same semaphore.
			await DisposeRefreshTimer().ConfigureAwait(false);

			_refreshTimer = new Timer(async _ =>
			{
				using var refreshCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(_disposeCancellationTokenSource.Token);
				refreshCancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(5));

				await RefreshQuotes(refreshCancellationTokenSource.Token).ConfigureAwait(false);
			});

			_refreshTimer.Change(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
		}
		finally
		{
			_timerSemaphore.Release();
		}
	}

	async ValueTask StopRefreshTimer()
	{
		await _timerSemaphore.WaitAsync(CancellationToken.None).ConfigureAwait(false);

		try
		{
			await DisposeRefreshTimer().ConfigureAwait(false);
		}
		finally
		{
			_timerSemaphore.Release();
		}
	}

	// Must only be called while the semaphore is already held
	async ValueTask DisposeRefreshTimer()
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

					// One call, so no update is lost. Not atomic, though: this delegate
					// runs outside the dictionary's lock and can run more than once, so
					// it only compares and returns. Keep side effects out of it.
					_latestQuotes.AddOrUpdate(
						symbol,
						quote,
						(_, existing) => quote.Timestamp > existing.Timestamp ? quote : existing);

					// Atomic increment, safe from every thread
					Interlocked.Increment(ref _refreshCount);
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
		// ConcurrentBag collects results from parallel workers without a lock
		ConcurrentBag<StockSymbolModel> symbols = [];

		Parallel.ForEach(MarketDataService.Symbols, symbol =>
		{
			_latestQuotes.TryGetValue(symbol, out var quote);

			symbols.Add(new StockSymbolModel(symbol, MarketDataService.GetCompanyName(symbol), quote));
		});

		return [.. symbols.OrderBy(static symbol => symbol.Symbol)];
	}
}