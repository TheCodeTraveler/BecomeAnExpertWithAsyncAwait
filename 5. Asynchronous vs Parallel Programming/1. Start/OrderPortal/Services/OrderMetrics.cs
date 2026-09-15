namespace OrderPortal;

// Registered as a singleton. Every concurrent checkout updates this same instance.
public sealed class OrderMetrics
{
	int _ordersPlaced;
	decimal _revenue;

	// ToDo Refactor (Step 1): an int read can be stale. The page should read the latest count,
	// not one cached on another thread.
	public int OrdersPlaced => _ordersPlaced;

	// ToDo Refactor (Step 2): reads can be wrong too. A decimal is 16 bytes, and this
	// can read one while another thread is halfway through writing it.
	public decimal Revenue => _revenue;

	public void RecordOrder(decimal orderTotal)
	{
		// ToDo Refactor (Step 1): `++` is a read, an add, and a write. Two threads can read the
		// same value before either writes, and one order silently disappears.
		_ordersPlaced++;

		// ToDo Refactor (Step 2): `+=` on a decimal is a much wider read, modify, write than
		// `++` on an int, and Interlocked has no overload for decimal.
		_revenue += orderTotal;
	}

	// ToDo Refactor (Step 1): Reset() runs at the start of every burst, so it
	// touches the same order count as RecordOrder().
	// ToDo Refactor (Step 2): it touches the same revenue total too.
	public void Reset()
	{
		_ordersPlaced = 0;
		_revenue = 0;
	}
}