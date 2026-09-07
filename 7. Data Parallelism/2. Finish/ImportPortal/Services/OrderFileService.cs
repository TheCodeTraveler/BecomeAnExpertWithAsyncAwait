namespace ImportPortal;

// Stands in for the uploaded CSV
public sealed class OrderFileService
{
	static readonly string[] _regions = ["US-CA", "US-NY", "US-TX", "US-WA"];

	public IReadOnlyList<OrderRow> ReadOrders(int rowCount)
	{
		var orders = new List<OrderRow>(rowCount);

		for (var rowNumber = 1; rowNumber <= rowCount; rowNumber++)
		{
			orders.Add(new OrderRow(
				rowNumber,
				$"SKU-{1000 + rowNumber % 250}",
				_regions[rowNumber % _regions.Length],
				20m + rowNumber % 400));
		}

		return orders;
	}
}