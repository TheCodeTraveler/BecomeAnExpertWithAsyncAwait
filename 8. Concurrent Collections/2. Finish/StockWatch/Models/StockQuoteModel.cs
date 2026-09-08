namespace StockWatch;

public record StockQuoteModel(
	string Symbol,
	decimal CurrentPrice,
	decimal PreviousClosePrice,
	DateTimeOffset Timestamp)
{
	public decimal Change => CurrentPrice - PreviousClosePrice;

	public decimal PercentChange => PreviousClosePrice is 0 ? 0 : Change / PreviousClosePrice * 100;
}