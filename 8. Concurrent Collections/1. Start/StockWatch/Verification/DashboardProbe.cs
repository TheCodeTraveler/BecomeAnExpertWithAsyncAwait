using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using StockWatch.Components.Pages;

namespace StockWatch;

// Workshop plumbing: the Dashboard page with your code-behind, rendered by the checks instead of a browser.
// It paints only the quotes applied count. In the browser, a render that throws swaps the dashboard for a Feed fault panel,
// but a check needs the exception itself, so the steps call Symbols directly, where an exception can be caught and reported.
public sealed class DashboardProbe : Dashboard
{
	// Hands the rendered instance to the check before OnInitializedAsync() starts loading quotes
	[Parameter, EditorRequired]
	public required Action<DashboardProbe> Initialized { get; set; }

	protected override void OnInitialized() => Initialized(this);

	protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, RefreshCount);
}