using Microsoft.AspNetCore.Components;

namespace CoffeeShop.Components.Layout;

public partial class StepNavBase : ComponentBase
{
	// A parameter rather than [Inject]: a reference-type parameter makes the nav re-render every time its page does
	[Parameter, EditorRequired]
	public required StepVerifier Verifier { get; set; }
}