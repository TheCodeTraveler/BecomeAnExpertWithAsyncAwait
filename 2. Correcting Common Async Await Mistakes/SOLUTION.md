# Solution Walkthrough: Correcting Common Async Await Mistakes

Use this during the guided walkthrough after the challenge and group review in [README.md](README.md).

The completed project is **2. Finish/HackerNews**. Compare your decisions with the finished sample as we rebuild the solution step by step.

## 1. Constructor Startup Work

The starter project calls an async method from the constructor without observing the returned task. That is the first warning to investigate.

An `async void` helper can appear to solve the compiler warning, but it creates a different problem: callers cannot await it, and exceptions cannot be observed through a returned `Task`.

For framework callbacks that cannot return `Task`, use safe fire-and-forget and log the exception path:

```cs
Refresh(CancellationToken.None).SafeFireAndForget(ex => Trace.WriteLine(ex));
```

The important rule is not that every fire-and-forget call is bad. The rule is that fire-and-forget work must be deliberate, exception-aware, and limited to places where the caller cannot await.

## 2. Forward Cancellation Tokens

When an async method receives a `CancellationToken`, pass it to async APIs that support cancellation:

```cs
var minimumRefreshTimeTask = Task.Delay(TimeSpan.FromSeconds(2), token);
```

This lets refresh work stop cleanly when the caller cancels the operation.

## 3. Use ConfigureAwait Intentionally

Use `ConfigureAwait(false)` when the continuation does not need the captured context:

```cs
var topStoriesList = await GetTopStories(token, StoriesConstants.NumberOfStories).ConfigureAwait(false);
```

After `ConfigureAwait(false)`, do not assume the continuation is running on the original UI context.

## 4. Replace Blocking Waits

Do not use `.Wait()` or `.Result` inside async code. Await the task instead:

```cs
await minimumRefreshTimeTask.ConfigureAwait(false);
```

Blocking waits can cause deadlocks, thread starvation, and poor responsiveness.

## 5. Stream Stories

The completed refresh pipeline streams stories instead of waiting for a fully materialized list before processing results:

```cs
[RelayCommand]
async Task Refresh(CancellationToken token)
{
    TopStoryCollection.Clear();

    var minimumRefreshTimeTask = Task.Delay(TimeSpan.FromSeconds(2), token);

    try
    {
        await foreach (var story in GetTopStories(StoriesConstants.NumberOfStories, token).ConfigureAwait(false))
        {
            if (!TopStoryCollection.Any(x => x.Title.Equals(story.Title, StringComparison.Ordinal)))
            {
                InsertIntoSortedCollection(TopStoryCollection, (a, b) => b.Score.CompareTo(a.Score), story);
            }
        }
    }
    catch (Exception e)
    {
        OnPullToRefreshFailed(e.ToString());
    }
    finally
    {
        await minimumRefreshTimeTask.ConfigureAwait(false);
        IsListRefreshing = false;
    }
}
```

The stream itself uses `IAsyncEnumerable<StoryModel>`:

```cs
async IAsyncEnumerable<StoryModel> GetTopStories(int storyCount, [EnumeratorCancellation] CancellationToken token)
{
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(storyCount);

    var topStoryIds = await _hackerNewsApiService.GetTopStoryIDs(token).ConfigureAwait(false);
    var getTopStoryTaskList = topStoryIds.Select(id => _hackerNewsApiService.GetStory(id, token)).ToList();

    await foreach (var topStoryTask in getTopStoryTaskList.ToAsyncEnumerable().WithCancellation(token).ConfigureAwait(false))
    {
        yield return await topStoryTask.ConfigureAwait(false);

        if (--storyCount <= 0)
        {
            break;
        }
    }
}
```

## 6. Compare Against Finish

Compare your implementation with the completed file:

[2. Finish/HackerNews/ViewModels/NewsViewModel.cs](2.%20Finish/HackerNews/ViewModels/NewsViewModel.cs)

Focus on the reasons behind each change, not only whether your code is textually identical.
