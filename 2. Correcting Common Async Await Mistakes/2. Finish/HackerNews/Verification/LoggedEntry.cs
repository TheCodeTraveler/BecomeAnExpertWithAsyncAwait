namespace HackerNews;

public sealed record LoggedEntry(LogLevel Level, string Message, Exception? Exception);