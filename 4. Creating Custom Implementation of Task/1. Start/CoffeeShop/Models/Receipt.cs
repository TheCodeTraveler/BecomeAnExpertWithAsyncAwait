using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace CoffeeShop;

public sealed class Receipt(Order order)
{
	static readonly TimeSpan _renderTime = TimeSpan.FromMilliseconds(500);

	volatile bool _isRendered;

	public Order Order { get; } = order;

	public bool IsRendered => _isRendered;

	// Formats the receipt lines and builds the QR code for the loyalty app.
	// This is CPU-bound work: it keeps a thread busy for about 500 ms, and nothing here waits on I/O.
	public void Render()
	{
		var receiptText = Encoding.UTF8.GetBytes($"{Order.CustomerName}: {Order.Drink}");
		var stopwatch = Stopwatch.StartNew();

		while (stopwatch.Elapsed < _renderTime)
		{
			receiptText = SHA256.HashData(receiptText);
		}

		_isRendered = true;
	}
}