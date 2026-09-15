# Solution Walkthrough: Correcting Common Async Await Mistakes

Use this during the guided walkthrough after the challenge and group review in [README.md](README.md).

The completed project is **2. Finish/HackerNews**. Compare your decisions with the finished Blazor sample as we rebuild the solution step by step. Each section below names the workshop step it makes pass, so you can follow along in the workshop guide.

**1. Start** and **2. Finish** share the same workshop plumbing: the `Steps` and `Verification` folders, and the layout with its workshop guide. The only file you change is `Components/Pages/News.razor.cs`.

## 1. Prefer Blazor Lifecycle Tasks (Step 1)

The starter project begins refresh work from `OnInitialized()` without observing the returned task:

```cs
protected override void OnInitialized()
{
    IsListRefreshing = true;

    // ToDo Refactor (Step 1): this starts the refresh and forgets it. OnInitialized() returns straight away,
    // so Blazor treats the page as initialized while the stories are still loading, and nothing observes the task.
    RefreshAsync(_disposeCancellationTokenSource.Token);
}
```

Using `async void` can appear to solve the compiler warning, but it creates a different problem: callers cannot await it, exceptions cannot be observed through a returned `Task`, and Blazor cannot track the asynchronous lifecycle work. When async/await is not available in a synchronous code path, another option is an extension method like [`.SafeFireAndForget()`](https://www.nuget.org/packages/AsyncAwaitBestPractices/).

However, Blazor offers `OnInitializedAsync()`, the asynchronous version of `OnInitialized()`, so let's use that:

```cs
protected override async Task OnInitializedAsync()
{
    IsListRefreshing = true;
    await RefreshAsync(_disposeCancellationTokenSource.Token);
}
```

[Safe fire-and-forget](https://www.nuget.org/packages/AsyncAwaitBestPractices/) still has a place when an API truly cannot return `Task`, but Blazor lifecycle methods already have `Task`-returning alternatives.

Step 1 renders the page the way prerendering does and waits for Blazor to finish initializing it. The starter finishes initializing with 0 of 50 stories loaded; with `OnInitializedAsync()`, initialization ends after the last story arrives. The step also reads the compiled page for `async void` methods, including async lambdas, and refuses to render the page while one exists, because an exception that escapes one can end the whole app.

## 2. Use ConfigureAwait Intentionally (Step 2)

Use `ConfigureAwait(false)` when the continuation does not need the captured context:

```cs
var topStoryIds = await GetTopStoryIDs(token).ConfigureAwait(false);
```

After `ConfigureAwait(false)`, do not update component state directly. Use `InvokeAsync(...)` to marshal UI updates through Blazor's renderer.

```cs
await InvokeAsync(TopStoryCollection.Clear);
```

In the starter, the `await GetTopStories(...)` in `RefreshAsync(CancellationToken)` captures Blazor's context, so everything after it, including the `finally` block, runs on the renderer for that browser tab. Steps 1, 2, and 4 also fail when a refresh against the in-memory Hacker News logs an error, and it names the `InvalidOperationException` Blazor throws when `StateHasChanged()` runs off the renderer.

## 3. Replace Blocking Waits (Step 2)

Do not use `.Wait()` or `.Result` inside async code. Await the task instead:

```cs
await minimumRefreshTimeTask.ConfigureAwait(ConfigureAwaitOptions.None | ConfigureAwaitOptions.SuppressThrowing);
```

Blocking waits can cause thread starvation, deadlocks, and poor responsiveness.

In the starter, `minimumRefreshTimeTask.Wait()` runs on Blazor's renderer, so nothing else for that browser tab can run until the 2 second delay ends. Step 2 presses Refresh and posts a tiny piece of work to the renderer every 25 milliseconds: a refresh that still calls `Wait()` makes one of them wait almost 2 seconds, and the finished page picks each one up within a millisecond or two. Timing alone would miss a `.Wait()` that `ConfigureAwait(false)` has moved onto a thread pool thread, where it still blocks a thread for every refresh, so the step also reads the compiled page for `Wait()`, `Result` and `GetAwaiter().GetResult()`. It checks that the refresh indicator still stays up for the full 2 second minimum, so deleting the wait does not pass either.

`ConfigureAwaitOptions.None` is the equivalent of `ConfigureAwait(false)`. The `SuppressThrowing` half of that line matters once the delay can be canceled, in the next section.

## 4. Forward Cancellation Tokens (Step 3)

When an async method receives a `CancellationToken`, pass it to async APIs that support cancellation:

```cs
var minimumRefreshTimeTask = Task.Delay(TimeSpan.FromSeconds(2), token);
```

The component cancels `_disposeCancellationTokenSource` when it is disposed. Forwarding that token lets refresh work stop when the user navigates away.

Step 3 presses Refresh, then closes the page while stories are still loading. The starter keeps its refresh alive for the rest of its 2 second minimum; with the token forwarded, the refresh ends within milliseconds and asks Hacker News for nothing more.

Once the delay can be canceled, awaiting it throws when the page goes away, and that `await` is in a `finally` block:

```cs
await minimumRefreshTimeTask.ConfigureAwait(ConfigureAwaitOptions.None | ConfigureAwaitOptions.SuppressThrowing);
```

`ConfigureAwaitOptions` is the .NET 8 overload of `ConfigureAwait` for when you need more than a context decision:

- `ConfigureAwaitOptions.None` is the equivalent of `ConfigureAwait(false)`: the continuation does not capture the current context.
- `ConfigureAwaitOptions.SuppressThrowing` completes the `await` without throwing when the task is canceled or faulted, and marks the exception as observed.

The delay is canceled when the component is disposed, so `SuppressThrowing` replaces a `try`/`catch (OperationCanceledException)` around the `await` whose only job was to swallow that cancellation. `Task.Delay` can only complete successfully or canceled, so nothing else is hidden.

`SuppressThrowing` is only supported on the non-generic `Task`. Using it on a `Task<TResult>` throws `ArgumentOutOfRangeException`, because there would be no result to return.

Step 3 fails when any exception escapes the refresh after the page closes. The other cancellable calls in the refresh are handled by catching `OperationCanceledException` only when the refresh's own token was canceled:

```cs
catch (OperationCanceledException e) when (token.IsCancellationRequested)
{
    Logger.LogError(e, "Refresh timed out.");
    await InvokeAsync(() => RefreshErrorMessage = "Unable to refresh top stories. Check your connection and try again.");
}
```

The step also loads the page while every Hacker News call throws `HttpRequestException`. The page has to show a generic, actionable message with no exception text in it, log the full exception on the server, and turn the refresh indicator off. The starter already does all three, and the step makes sure your refactor keeps it that way.

## 5. Stream Stories (Step 4)

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
    catch (OperationCanceledException e) when (token.IsCancellationRequested)
    {
        Logger.LogError(e, "Refresh timed out.");
        await InvokeAsync(() => RefreshErrorMessage = "Unable to refresh top stories. Check your connection and try again.");
    }
    catch (Exception e)
    {
        Logger.LogError(e, "Failed to refresh Hacker News top stories.");
        await InvokeAsync(() => RefreshErrorMessage = "Unable to refresh top stories. Check your connection and try again.");
    }
    finally
    {
        await minimumRefreshTimeTask.ConfigureAwait(ConfigureAwaitOptions.None | ConfigureAwaitOptions.SuppressThrowing);

        await InvokeAsync(() =>
        {
            IsListRefreshing = false;

            if (!token.IsCancellationRequested)
                StateHasChanged();
        });
    }
}
```

The stream itself uses `IAsyncEnumerable<StoryModel>`:

```cs
async IAsyncEnumerable<StoryModel> GetTopStories(IReadOnlyList<long> topStoryIds, int storyCount, [EnumeratorCancellation] CancellationToken token)
{
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(storyCount);

    var storyIds = topStoryIds.Take(storyCount).ToList();
    var getTopStoryTaskList = storyIds.Select(id => GetStory(id, token)).ToAsyncEnumerable();

    await foreach (var topStoryTask in getTopStoryTaskList.WithCancellation(token).ConfigureAwait(false))
    {
        yield return await topStoryTask.ConfigureAwait(false);
    }
}
```

Step 4 holds back the 50th story and looks at the rendered page once every other story has arrived. The starter still shows the loading skeleton; the streaming page already shows 49 stories. When the last story arrives, the step checks that the list has all 50 stories, sorted by score, with none twice.

## 6. Return Task Directly (Step 5)

When a method only forwards another task, return that task directly:

```cs
Task<StoryModel> GetStory(long storyId, CancellationToken token) => HackerNewsApiService.GetStory(storyId, token);
```

This avoids an unnecessary async state machine.

## 7. Use ValueTask for the Synchronous Hot Path (Step 5)

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

Step 5 reads the compiled page: `GetStory(...)` must return `Task<StoryModel>` without an async state machine, and `GetTopStoryIDs(...)` must return `ValueTask<IReadOnlyList<long>>`. It then loads the page and calls `GetTopStoryIDs(...)` the way a second refresh does, and checks that it completed synchronously without calling Hacker News.

## 8. Run It and Compare Against Finish

Run the finished app on [http://localhost:5002](http://localhost:5002). The workshop guide shows 5 of 5 steps pass once the checks that run after the app starts have finished. On **Top stories** beside it, press **Refresh**, and watch the stories stream into place.

Compare your implementation with the completed file:

[2. Finish/HackerNews/Components/Pages/News.razor.cs](2.%20Finish/HackerNews/Components/Pages/News.razor.cs)

Focus on the reasons behind each change, not only whether your code is textually identical.
