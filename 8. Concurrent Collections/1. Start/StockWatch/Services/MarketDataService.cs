namespace StockWatch;

// A simulated market feed. It behaves like a remote quote API, including a
// short asynchronous delay, but needs no API key and no network connection.
public sealed class MarketDataService
{
	static readonly IReadOnlyDictionary<string, string> _companyNames = new Dictionary<string, string>
	{
		{ "AAPL", "Apple Inc." },
		{ "MSFT", "Microsoft Corp." },
		{ "GOOGL", "Alphabet Inc." },
		{ "AMZN", "Amazon.com Inc." },
		{ "NVDA", "NVIDIA Corp." },
		{ "META", "Meta Platforms" },
		{ "TSLA", "Tesla Inc." },
		{ "BRK.B", "Berkshire Hathaway" },
		{ "LLY", "Eli Lilly & Co." },
		{ "AVGO", "Broadcom Inc." },
		{ "V", "Visa Inc." },
		{ "JPM", "JPMorgan Chase" },
		{ "WMT", "Walmart Inc." },
		{ "XOM", "Exxon Mobil" },
		{ "UNH", "UnitedHealth Group" },
		{ "MA", "Mastercard Inc." },
		{ "ORCL", "Oracle Corp." },
		{ "HD", "Home Depot Inc." },
		{ "PG", "Procter & Gamble" },
		{ "COST", "Costco Wholesale" },
		{ "JNJ", "Johnson & Johnson" },
		{ "NFLX", "Netflix Inc." },
		{ "ABBV", "AbbVie Inc." },
		{ "BAC", "Bank of America" },
		{ "CRM", "Salesforce Inc." },
		{ "MRK", "Merck & Co." },
		{ "CVX", "Chevron Corp." },
		{ "KO", "Coca-Cola Co." },
		{ "ADBE", "Adobe Inc." },
		{ "AMD", "Advanced Micro" },
		{ "PEP", "PepsiCo Inc." },
		{ "TMO", "Thermo Fisher" },
		{ "MCD", "McDonald's Corp." },
		{ "CSCO", "Cisco Systems" },
		{ "ACN", "Accenture plc" },
		{ "ABT", "Abbott Labs" },
		{ "LIN", "Linde plc" },
		{ "DHR", "Danaher Corp." },
		{ "TXN", "Texas Instruments" },
		{ "VZ", "Verizon Comm." },
		{ "INTU", "Intuit Inc." },
		{ "QCOM", "Qualcomm Inc." },
		{ "PM", "Philip Morris" },
		{ "IBM", "IBM Corp." },
		{ "GE", "General Electric" },
		{ "CAT", "Caterpillar Inc." },
		{ "NOW", "ServiceNow Inc." },
		{ "AMAT", "Applied Materials" },
		{ "NEE", "NextEra Energy" },
		{ "UBER", "Uber Tech." },
		{ "RTX", "RTX Corp." },
		{ "SPGI", "S&P Global" },
		{ "HON", "Honeywell Intl." },
		{ "BKNG", "Booking Holdings" },
		{ "LOW", "Lowe's Cos." },
		{ "UNP", "Union Pacific" },
		{ "BLK", "BlackRock Inc." },
		{ "T", "AT&T Inc." },
		{ "SYK", "Stryker Corp." },
		{ "ELV", "Elevance Health" },
	};

	// A stable hash so every attendee sees the same starting prices.
	// string.GetHashCode() is randomized per process and would change every run.
	readonly Dictionary<string, decimal> _previousCloses = _companyNames.Keys
		.ToDictionary(static symbol => symbol, static symbol => 50m + StableHash(symbol) % 450);

	public IReadOnlyCollection<string> Symbols => _companyNames.Keys.ToList();

	public string GetCompanyName(string symbol) => _companyNames.TryGetValue(symbol, out var name) ? name : symbol;

	// Reads like a remote call: it yields, then returns a fresh quote
	public async Task<StockQuoteModel> GetStockQuote(string symbol, CancellationToken token)
	{
		await Task.Delay(Random.Shared.Next(10, 60), token).ConfigureAwait(false);

		var previousClose = _previousCloses[symbol];
		var drift = (decimal)((Random.Shared.NextDouble() - 0.5) * 0.06);
		var currentPrice = Math.Round(previousClose * (1 + drift), 2);

		return new StockQuoteModel(symbol, currentPrice, previousClose, DateTimeOffset.UtcNow);
	}

	static int StableHash(string value)
	{
		var hash = 17;

		foreach (var character in value)
		{
			hash = (hash * 31 + character) & 0x7FFFFFFF;
		}

		return hash;
	}
}