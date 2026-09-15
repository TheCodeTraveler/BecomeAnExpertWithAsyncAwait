using Microsoft.AspNetCore.Components;

namespace InternalsLab.Components.Layout;

// A link that opens a line of this app's source code on GitHub or in VS Code, shown as File.cs:line
public partial class CodeLinkBase : ComponentBase
{
	[Parameter, EditorRequired]
	public required CodeLocation Location { get; set; }

	[Parameter, EditorRequired]
	public CodeEditor Editor { get; set; }

	// Shown instead of the file name, for example the file's full path
	[Parameter]
	public string? Label { get; set; }

	// GitHub opens in a new tab so the step page keeps your place. VS Code opens in the editor, so the page stays where it is anyway.
	public string? Target => Editor is CodeEditor.GitHub ? "_blank" : null;

	public string? Rel => Editor is CodeEditor.GitHub ? "noopener" : null;

	public string Title => (Location.Line, Editor) switch
	{
		({ } line, CodeEditor.VisualStudioCode) => $"Open {Location.File} at line {line} in VS Code",
		({ } line, _) => $"Open {Location.File} at line {line} on GitHub",
		(null, CodeEditor.VisualStudioCode) => $"Open {Location.File} in VS Code",
		(null, _) => $"Open {Location.File} on GitHub",
	};
}
