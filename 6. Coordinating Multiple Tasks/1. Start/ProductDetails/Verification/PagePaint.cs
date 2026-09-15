namespace ProductDetails;

// One time the Product page painted, with every card exactly as that paint drew it
public sealed record PagePaint(TimeSpan Elapsed, IReadOnlyList<PanelState> Panels)
{
	public int AnsweredCards => Panels.Count(static panel => panel.Status is not "waiting");

	public string? StatusOf(string name) => Panels.FirstOrDefault(panel => panel.Name == name)?.Status;
}