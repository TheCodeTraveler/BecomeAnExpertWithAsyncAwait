namespace ProductDetails;

// One load of the Product page, as a step saw it.
// Elapsed is the wall clock time from the start of the load until the page finished loading. LongestRendererPause is the longest Blazor's renderer was held up while it loaded.
// Panels, PageError, TotalSeconds, IsLoading and Html are read from the page after it finished loading, and are empty when it never did.
public sealed record PageLoad(
	bool Finished,
	TimeSpan Elapsed,
	TimeSpan LongestRendererPause,
	IReadOnlyList<PagePaint> Paints,
	IReadOnlyList<PanelState> Panels,
	string? PageError,
	double? TotalSeconds,
	bool IsLoading,
	string Html)
{
	public PanelState? FindPanel(string name) => Panels.FirstOrDefault(panel => panel.Name == name);
}