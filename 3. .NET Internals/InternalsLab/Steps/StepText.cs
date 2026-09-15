using System.Text.RegularExpressions;

namespace InternalsLab;

// Step text can contain links, written like Markdown links:
// [Program.cs](Program.cs) links to a file, [Program.cs](Program.cs#text on the line) links to the first line containing that text (see SourceCode.Find),
// and [Click Try it](#try-it) scrolls to a panel on the same page. A label cannot contain [ or ], and a target cannot contain ( or ).
public static partial class StepText
{
	public static IReadOnlyList<TextSegment> Parse(string text)
	{
		var segments = new List<TextSegment>();
		var index = 0;

		foreach (Match match in Link().Matches(text))
		{
			if (match.Index > index)
				segments.Add(new TextSegment(text[index..match.Index], null));

			segments.Add(new TextSegment(match.Groups["label"].Value, match.Groups["target"].Value));
			index = match.Index + match.Length;
		}

		if (index < text.Length)
			segments.Add(new TextSegment(text[index..], null));

		return segments;
	}

	[GeneratedRegex(@"\[(?<label>[^\[\]]+)\]\((?<target>[^()]+)\)")]
	private static partial Regex Link();
}