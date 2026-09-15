namespace InternalsLab;

// One piece of step text. Target is null for plain text, starts with # for a panel on the same page, and is otherwise a file with an optional #marker.
public sealed record TextSegment(string Text, string? Target);