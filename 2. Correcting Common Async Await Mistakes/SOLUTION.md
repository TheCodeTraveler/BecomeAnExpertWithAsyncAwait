# Solution Walkthrough: Correcting Common Async Await Mistakes

Use this during the guided walkthrough after the challenge and group review in [README.md](README.md).

The completed project is **2. Finish/HackerNews**. Compare your decisions with the finished Blazor sample as we rebuild the solution step by step.

## 1. Prefer Blazor Lifecycle Tasks

The starter project begins refresh work from `OnInitialized()` without observing the returned task:

```cs
protected override void OnInitialized()
{
    IsListRefreshing = true;

    //ToDo Refactor
    RefreshAsync(_disposeCancellationTokenSource.Token);
}
```

An `async void` helper can appear to solve the compiler warning, but it creates a different problem: callers cannot await it, exceptions cannot be observed through a returned `Task`, and Blazor cannot track the asynchronous lifecycle work.

Use the asynchronous Blazor lifecycle method instead:

```cs
protected override async Task OnInitializedAsync()
{
    IsListRefreshing = true;
    await RefreshAsync(_disposeCancellationTokenSource.Token);
}
```

Safe fire-and-forget still has a place when an API truly cannot return `Task`, but Blazor lifecycle methods already have `Task`-returning alternatives.

## 2. Forward Cancellation Tokens

When an async method receives a `CancellationToken`, pass it to async APIs that support cancellation:

```cs
var minimumRefreshTimeTask = Task.Delay(TimeSpan.FromSeconds(2), token);
```

The component cancels `_disposeCancellationTokenSource` when it is disposed. Forwarding that token lets refresh work stop when the user navigates away.

## 3. Use ConfigureAwait Intentionally

Use `ConfigureAwait(false)` when the continuation does not need the captured context:

```cs
var topStoryIds = await GetTopStoryIDs(token).ConfigureAwait(false);
```

After `ConfigureAwait(false)`, do not update component state directly. Use `InvokeAsync(...)` to marshal UI updates through Blazor's renderer.

```cs
await InvokeAsync(TopStoryCollection.Clear);
```

## 4. Replace Blocking Waits

Do not use `.Wait()` or `.Result` inside async code. Await the task instead:

```cs
try
{
    await minimumRefreshTimeTask.ConfigureAwait(false);
}
catch (OperationCanceledException) when (token.IsCancellationRequested)
{
}
```

Blocking waits can cause thread starvation, deadlocks, and poor responsiveness.

## 5. Stream Stories

The completed refresh pipeline fetches top story IDs, clears the UI through Blazor's renderer, and then streams stories into the list:

```cs
async Task RefreshAsync(CancellationToken token)
{
    IsListRefreshing = true;
    RefreshErrorMessage = null;
    await InvokeAsync(StateHasChanged);

    var minimumRefreshTimeTask = Task.Delay(TimeSpan.FromSeconds(2), token);

    try
    {
        var topStoryIds = await GetTopStoryIDs(token).ConfigureAwait(false);

        await InvokeAsync(TopStoryCollection.Clear);

        await foreach (var story in GetTopStories(topStoryIds, StoriesConstants.NumberOfStories, token).ConfigureAwait(false))
        {
            await InvokeAsync(() =>
            {
                if (!TopStoryCollection.Any(x => x.Title.Equals(story.Title, StringComparison.Ordinal)))
                {
                    InsertIntoSortedList(TopStoryCollection, (a, b) => b.Score.CompareTo(a.Score), story);
                }

                StateHasChanged();
            });
        }
    }
    catch (OperationCanceledException) when (token.IsCancellationRequested)
    {
    }
    catch (Exception e)
    {
        Logger.LogError(e, "Failed to refresh Hacker News top stories.");
        await InvokeAsync(() => RefreshErrorMessage = "Unable to refresh top stories. Check your connection and try again.");
    }
    finally
    {
        try
        {
            await minimumRefreshTimeTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }

        if (!token.IsCancellationRequested)
        {
            await InvokeAsync(() =>
            {
                IsListRefreshing = false;
                StateHasChanged();
            });
        }
    }
}
```

The stream itself uses `IAsyncEnumerable<StoryModel>`:

```cs
async IAsyncEnumerable<StoryModel> GetTopStories(IReadOnlyList<long> topStoryIds, int storyCount, [EnumeratorCancellation] CancellationToken token)
{
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(storyCount);

    var storyIds = topStoryIds.Take(storyCount).ToList();
    var getTopStoryTaskList = storyIds.Select(id => GetStory(id, token)).ToList();

    foreach (var topStoryTask in getTopStoryTaskList)
    {
        token.ThrowIfCancellationRequested();

        yield return await topStoryTask.ConfigureAwait(false);
    }
}
```

## 6. Return Task Directly

When a method only forwards another task, return that task directly:

```cs
Task<StoryModel> GetStory(long storyId, CancellationToken token) => HackerNewsApiService.GetStory(storyId, token);
```

This avoids an unnecessary async state machine.

## 7. Use ValueTask for the Synchronous Hot Path

`GetTopStoryIDs(CancellationToken)` can return cached IDs synchronously when the current data is recent:

```cs
async ValueTask<IReadOnlyList<long>> GetTopStoryIDs(CancellationToken token)
{
    if (IsDataRecent(TimeSpan.FromHours(1)))
    {
        return TopStoryCollection.Select(x => x.Id).ToList();
    }

    try
    {
        return await HackerNewsApiService.GetTopStoryIDs(token).ConfigureAwait(false);
    }
    catch (Exception e)
    {
        Trace.WriteLine(e.Message);
        throw;
    }
}
```

Use `ValueTask` when the synchronous path is common enough to avoid allocating a `Task` for that path.

## 8. Compare Against Finish

Compare your implementation with the completed file:

[2. Finish/HackerNews/Components/Pages/News.razor.cs](2.%20Finish/HackerNews/Components/Pages/News.razor.cs)

Focus on the reasons behind each change, not only whether your code is textually identical.
