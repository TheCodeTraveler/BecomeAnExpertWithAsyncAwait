namespace ImportPortal;

// One row of an uploaded order file
public sealed class OrderRow(int rowNumber, string sku, string region, decimal amount)
{
	public int RowNumber { get; } = rowNumber;

	public string Sku { get; } = sku;

	public string Region { get; } = region;

	public decimal Amount { get; } = amount;

	// Filled in by the validation step
	public long RiskScore { get; set; }

	// Filled in by the enrichment step
	public string? CustomerTier { get; set; }
}

public record ImportReport(
	int RowsValidated,
	int RowsEnriched,
	TimeSpan ValidateElapsed,
	TimeSpan EnrichElapsed,
	TimeSpan ReportElapsed,
	IReadOnlyList<RegionTotal> RegionTotals);

public record RegionTotal(string Region, int Orders, decimal Revenue, long AverageRisk);