namespace InternalsLab;

public sealed record ExplainQuestion(string Id, string Question, IReadOnlyList<ExplainAnswer> Answers)
{
	public ExplainAnswer? FindAnswer(string? answerId) => Answers.FirstOrDefault(answer => answer.Id == answerId);
}