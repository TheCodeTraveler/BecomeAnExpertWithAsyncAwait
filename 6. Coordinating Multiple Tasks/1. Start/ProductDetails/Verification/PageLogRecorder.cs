using ProductDetails.Components.Pages;

namespace ProductDetails;

// Workshop plumbing: stands in for the Product page's ILogger while a step renders it, so the step can check what the page logged server-side.
// The page logs from thread pool threads while the step reads the messages, so every member takes the lock.
public sealed class PageLogRecorder : ILogger<ProductPageBase>
{
	readonly Lock _lock = new();
	readonly List<LoggedMessage> _messages = [];

	public IReadOnlyList<LoggedMessage> Messages
	{
		get
		{
			lock (_lock)
			{
				return [.. _messages];
			}
		}
	}

	public IDisposable? BeginScope<TState>(TState state)
		where TState : notnull => null;

	public bool IsEnabled(LogLevel logLevel) => true;

	public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
	{
		var message = new LoggedMessage(logLevel, formatter(state, exception), exception);

		lock (_lock)
		{
			_messages.Add(message);
		}
	}
}