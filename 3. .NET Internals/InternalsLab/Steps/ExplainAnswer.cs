namespace InternalsLab;

// Feedback is shown as soon as you choose this answer: a confirmation when it is correct, and a hint when it is not
public sealed record ExplainAnswer(string Id, string Text, bool IsCorrect, string Feedback);