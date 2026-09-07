namespace OrderPortal;

// Registered as a singleton. Every concurrent checkout updates this same instance.
public sealed class OrderMetrics
{
	// Interlocked has no decimal overload, so revenue is guarded by a lock.
	// The .NET 9 Lock type is a dedicated lock object rather than locking on `object`.
	readonly Lock _revenueLock = new();

	int _ordersPlaced;
	decimal _revenue;

	public int OrdersPlaced => Volatile.Read(ref _ordersPlaced);

	public decimal Revenue
	{
		get
		{
			lock (_revenueLock)
			{
				return _revenue;
			}
		}
	}

	public void RecordOrder(decimal orderTotal)
	{
		// One atomic instruction. Nothing can slip between the read and the write.
		Interlocked.Increment(ref _ordersPlaced);

		lock (_revenueLock)
		{
			_revenue += orderTotal;
		}
	}

	public void Reset()
	{
		Interlocked.Exchange(ref _ordersPlaced, 0);

		lock (_revenueLock)
		{
			_revenue = 0;
		}
	}
}