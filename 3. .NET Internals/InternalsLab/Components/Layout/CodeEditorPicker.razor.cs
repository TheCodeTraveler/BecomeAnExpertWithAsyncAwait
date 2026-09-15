using Microsoft.AspNetCore.Components;

namespace InternalsLab.Components.Layout;

// Chooses where the code links open. Use it with @bind-Editor, so the page that shows the links renders them again.
public partial class CodeEditorPickerBase : ComponentBase
{
	[Parameter, EditorRequired]
	public CodeEditor Editor { get; set; }

	[Parameter]
	public EventCallback<CodeEditor> EditorChanged { get; set; }

	protected string CssClass(CodeEditor editor) => editor == Editor ? "editor-option editor-option-selected" : "editor-option";

	protected string IsPressed(CodeEditor editor) => editor == Editor ? "true" : "false";

	protected Task ChooseAsync(CodeEditor editor) => EditorChanged.InvokeAsync(editor);
}
