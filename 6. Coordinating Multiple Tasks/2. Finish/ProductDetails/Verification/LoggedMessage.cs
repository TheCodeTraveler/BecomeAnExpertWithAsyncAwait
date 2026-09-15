namespace ProductDetails;

public sealed record LoggedMessage(LogLevel Level, string Message, Exception? Exception);