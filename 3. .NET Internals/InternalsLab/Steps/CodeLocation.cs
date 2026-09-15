namespace InternalsLab;

// A place in this app's source code that a step page links to. File is relative to the InternalsLab project folder.
// Line is the line of code to open, and FirstLine is where the comment above it starts. Both are null when the line could not be found, so the link opens the file.
public sealed record CodeLocation(string File, int? FirstLine, int? Line)
{
	const string _gitHubFolderUrl = "https://github.com/TheCodeTraveler/BecomeAnExpertWithAsyncAwait/blob/main/";

	public string FileName => Path.GetFileName(File);

	public string GetUrl(CodeEditor editor)
	{
		if (editor is CodeEditor.VisualStudioCode)
		{
			// vscode://file/{full path}:{line}:{column} opens the file at that line, on Windows and macOS
			var fullPath = Path.GetFullPath(Path.Combine(SourceCode.ProjectDirectory, File));

			return $"vscode://file/{EscapePath(fullPath).TrimStart('/')}{(Line is { } line ? $":{line}:1" : string.Empty)}";
		}

		// GitHub highlights the comment above the line too, when there is one
		var sectionFolder = Path.GetFileName(Path.GetDirectoryName(SourceCode.ProjectDirectory)) ?? string.Empty;
		var projectFolder = Path.GetFileName(SourceCode.ProjectDirectory);
		var lineAnchor = (FirstLine, Line) switch
		{
			({ } firstLine, { } line) when firstLine < line => $"#L{firstLine}-L{line}",
			(_, { } line) => $"#L{line}",
			_ => string.Empty,
		};

		return $"{_gitHubFolderUrl}{EscapePath($"{sectionFolder}/{projectFolder}/{File}")}{lineAnchor}";
	}

	// Escapes every folder name, such as "3. .NET Internals", but keeps a Windows drive letter such as C: readable
	static string EscapePath(string path) => string.Join('/', path.Replace('\\', '/').Split('/')
		.Select(static segment => segment.EndsWith(':') ? segment : Uri.EscapeDataString(segment)));
}