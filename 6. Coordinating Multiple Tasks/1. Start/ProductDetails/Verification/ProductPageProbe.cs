using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using ProductDetails.Components.Pages;

namespace ProductDetails;

// Workshop plumbing: your Product page exactly as you wrote it, plus a record of every paint.
// Only ProductPageSession renders it. It has no route of its own, so the app never shows it.
public sealed class ProductPageProbe : Product
{
	[Parameter, EditorRequired]
	public required ProductPageSession Session { get; set; }

	// Does what clicking Load product page does. Blazor repaints a component as soon as its event handler returns, and again when the Task it returned completes.
	public async Task PressLoadProductPageAsync()
	{
		var load = LoadProductAsync();

		StateHasChanged();

		await load.ConfigureAwait(false);

		await InvokeAsync(StateHasChanged).ConfigureAwait(false);
	}

	protected override void OnInitialized() => Session.Attach(this);

	// Blazor calls this every time the page paints, so the cards recorded here are exactly the cards that paint draws
	protected override void BuildRenderTree(RenderTreeBuilder builder)
	{
		Session.RecordPaint(Panels);

		base.BuildRenderTree(builder);
	}
}