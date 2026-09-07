namespace StockWatch;

public record StockSymbolModel(string Symbol, string CompanyName, StockQuoteModel? Quote)
{
	public string TrendCssClass => Quote switch
	{
		null => "trend-flat",
		{ Change: > 0 } => "trend-up",
		{ Change: < 0 } => "trend-down",
		_ => "trend-flat"
	};
}