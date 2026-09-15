using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace InternalsLab;

// Workshop plumbing: finds lines in this app's source code, so the step pages can link straight to the code they talk about.
// The app runs on your machine from the folder it was built in, so the path the compiler recorded for this file shows where every other source file is.
// Files are read again whenever they change, so the links follow your edits.
public static partial class SourceCode
{
	static readonly ConcurrentDictionary<string, SourceFile> _files = new();

	public static string ProjectDirectory { get; } = GetProjectDirectory();

	// Finds the first line that contains marker. When that line is a comment, the location is the code right below it.
	public static CodeLocation Find(string file, string? marker = null)
	{
		var lines = ReadLines(file);
		var markerIndex = marker is null || lines is null ? -1 : Array.FindIndex(lines, line => line.Contains(marker, StringComparison.Ordinal));

		return markerIndex < 0 || lines is null ? new CodeLocation(file, null, null) : ToLocation(file, lines, markerIndex);
	}

	// Every checkpoint is recorded right below a "// Step N: checkpoint M." comment, or "// Step N: checkpoints M and O." when two checkpoints share a line
	public static CodeLocation FindCheckpoint(WorkshopStep step, int checkpoint)
	{
		var lines = ReadLines(step.ExperimentFile);

		for (var index = 0; lines is not null && index < lines.Length; index++)
		{
			var match = CheckpointComment().Match(lines[index]);

			if (match.Success
				&& int.Parse(match.Groups["step"].Value) == step.Number
				&& match.Groups["checkpoints"].Value.Split(" and ").Contains(checkpoint.ToString()))
			{
				return ToLocation(step.ExperimentFile, lines, index);
			}
		}

		return new CodeLocation(step.ExperimentFile, null, null);
	}

	// Stack traces name every source file by its full path. Relative paths such as Experiments/ExecutionContextExperiment.cs are easier to read.
	public static string WithRelativePaths(string text) => text.Replace(ProjectDirectory + Path.DirectorySeparatorChar, string.Empty, StringComparison.Ordinal);

	static CodeLocation ToLocation(string file, string[] lines, int markerIndex)
	{
		var codeIndex = markerIndex;

		while (codeIndex < lines.Length - 1 && IsCommentOrBlank(lines[codeIndex]))
			codeIndex++;

		return new CodeLocation(file, markerIndex + 1, codeIndex + 1);
	}

	static bool IsCommentOrBlank(string line) => string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("//", StringComparison.Ordinal);

	static string[]? ReadLines(string file)
	{
		var path = Path.GetFullPath(Path.Combine(ProjectDirectory, file));

		try
		{
			var lastWriteTime = File.GetLastWriteTimeUtc(path);

			if (_files.TryGetValue(path, out var sourceFile) && sourceFile.LastWriteTime == lastWriteTime)
				return sourceFile.Lines;

			var lines = File.ReadAllLines(path);
			_files[path] = new SourceFile(lastWriteTime, lines);

			return lines;
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
			// The source code is not where it was built, so the links open the file without a line
			return null;
		}
	}

	// This file is Steps/SourceCode.cs, so the project folder is the folder above Steps
	static string GetProjectDirectory([CallerFilePath] string sourceFilePath = "") =>
		Path.GetDirectoryName(Path.GetDirectoryName(sourceFilePath)) ?? string.Empty;

	[GeneratedRegex(@"// Step (?<step>\d+): checkpoints? (?<checkpoints>\d+(?: and \d+)*)\.")]
	private static partial Regex CheckpointComment();

	sealed record SourceFile(DateTime LastWriteTime, string[] Lines);
}