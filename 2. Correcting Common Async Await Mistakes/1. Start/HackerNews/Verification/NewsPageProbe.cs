using HackerNews.Components.Pages;

namespace HackerNews;

// Workshop plumbing: your News page, plus a few read-only views of its state for the steps.
// It adds no behavior, so rendering it runs exactly the code the Top stories page runs.
// Read these members through NewsPageSession, which reads them on Blazor's renderer, the same way the page does.
public sealed class NewsPageProbe : News
{
	public IReadOnlyList<StoryModel> Stories => [.. TopStoryCollection];

	public bool IsRefreshing => IsListRefreshing;

	public string? ErrorMessage => RefreshErrorMessage;

	// What the Refresh button calls
	public Task Refresh() => RefreshAsync();
}