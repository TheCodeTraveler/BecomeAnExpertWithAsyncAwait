using HackerNews.Components.Pages;

namespace HackerNews;

// Workshop plumbing: the ILogger<NewsPageBase> a step gives your page, so it can check what the page logged on the server.
// Nothing is written to the app's log output, so the errors a step causes on purpose do not look like errors in your app.
public sealed class CapturingLogger : ILogger<NewsPageBase>
{
	readonly Lock _lock = new();
	readonly List<LoggedEntry> _entries = [];

	public IReadOnlyList<LoggedEntry> Entries
	{
		get
		{
			lock (_lock)
			{
				return [.. _entries];
			}
		}
	}

	public IReadOnlyList<LoggedEntry> Errors => [.. Entries.Where(static entry => entry.Level >= LogLevel.Error)];

	public IDisposable? BeginScope<TState>(TState state)
		where TState : notnull => null;

	public bool IsEnabled(LogLevel logLevel) => true;

	public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
	{
		var entry = new LoggedEntry(logLevel, formatter(state, exception), exception);

		lock (_lock)
		{
			_entries.Add(entry);
		}
	}
}