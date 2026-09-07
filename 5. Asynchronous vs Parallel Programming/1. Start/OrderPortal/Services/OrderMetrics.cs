namespace OrderPortal;

// Registered as a singleton. Every concurrent checkout updates this same instance.
public sealed class OrderMetrics
{
	int _ordersPlaced;
	decimal _revenue;

	public int OrdersPlaced => _ordersPlaced;

	public decimal Revenue => _revenue;

	// ToDo Refactor: `++` is a read, an add, and a write. Two threads can read the
	// same value before either writes, and one order silently disappears.
	public void RecordOrder(decimal orderTotal)
	{
		_ordersPlaced++;
		_revenue += orderTotal;
	}

	public void Reset()
	{
		_ordersPlaced = 0;
		_revenue = 0;
	}
}