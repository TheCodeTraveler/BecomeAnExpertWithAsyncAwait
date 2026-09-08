namespace OrderPortal;

// Guards the stock ledger. Reserving stock must be atomic, and every
// reservation must also be written to the audit trail.
public sealed class InventoryLedger : IDisposable
{
	// SemaphoreSlim is the right primitive here: `lock` cannot be held across an await.
	readonly SemaphoreSlim _ledgerSemaphore = new(1, 1);

	readonly Dictionary<string, int> _stockOnHand = new()
	{
		{ "SKU-1000", 5_000 },
		{ "SKU-2000", 5_000 },
		{ "SKU-3000", 5_000 },
	};

	readonly List<string> _auditTrail = [];

	public int AuditEntries => _auditTrail.Count;

	public void Dispose() => _ledgerSemaphore.Dispose();

	public async Task<bool> ReserveStockAsync(string sku, int quantity, CancellationToken token)
	{
		await _ledgerSemaphore.WaitAsync(token).ConfigureAwait(false);

		try
		{
			if (!_stockOnHand.TryGetValue(sku, out var onHand) || onHand < quantity)
			{
				return false;
			}

			// ToDo Refactor: this call also waits on _ledgerSemaphore, which this
			// method is already holding. SemaphoreSlim is not reentrant, so the
			// thread waits for a permit it will never release. That is a deadlock.
			await WriteAuditEntryAsync($"Reserved {quantity} of {sku}", token).ConfigureAwait(false);

			// The audit write above can be cancelled, so the decrement happens
			// after it. A cancelled call must not reduce stock with no audit entry.
			_stockOnHand[sku] = onHand - quantity;

			return true;
		}
		finally
		{
			_ledgerSemaphore.Release();
		}
	}

	public async Task WriteAuditEntryAsync(string entry, CancellationToken token)
	{
		await _ledgerSemaphore.WaitAsync(token).ConfigureAwait(false);

		try
		{
			// Pretend this writes to an audit table
			await Task.Delay(TimeSpan.FromMilliseconds(5), token).ConfigureAwait(false);

			_auditTrail.Add(entry);
		}
		finally
		{
			_ledgerSemaphore.Release();
		}
	}
}