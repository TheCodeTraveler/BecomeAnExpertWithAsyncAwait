# Correcting Common Async Await Mistakes

In this section we will correct common async/await mistakes using the Blazor **HackerNews** sample.

The **1. Start** folder contains the intentionally imperfect code attendees will edit. The **2. Finish** folder contains the completed Blazor version.

## 1. Open **HackerNews.slnx** in IDE

1. Using File Explorer (Windows) or Finder (macOS), navigate to **BecomeAnExpertWithAsyncAwait/2. Correcting Common Async Await Mistakes/1. Start**.
2. Open **HackerNews.slnx** in your IDE.
3. Open **HackerNews/Components/Pages/News.razor.cs**.

## 2. Using Async Void

1. In **News.razor.cs**, in `OnInitialized()`, scroll down to the first `//ToDo Refactor` comment:

```cs
protected override void OnInitialized()
{
    IsListRefreshing = true;

    //ToDo Refactor
    RefreshAsync(_disposeCancellationTokenSource.Token);
}
```

2. Below `OnInitialized()`, add an `async void RefreshOnInitialized()` method:

```cs
protected override void OnInitialized()
{
    IsListRefreshing = true;

    //ToDo Refactor
    RefreshAsync(_disposeCancellationTokenSource.Token);
}

async void RefreshOnInitialized()
{
    await RefreshAsync(_disposeCancellationTokenSource.Token);
}
```

3. In `OnInitialized()`, call the new method:

```cs
protected override void OnInitialized()
{
    IsListRefreshing = true;

    //ToDo Refactor
    RefreshOnInitialized();
}

async void RefreshOnInitialized()
{
    await RefreshAsync(_disposeCancellationTokenSource.Token);
}
```

> **Note:** `async void` is dangerous because callers cannot await it and exceptions cannot be observed through a returned `Task`. In Blazor, this also means the renderer cannot track the asynchronous lifecycle work.

## 3. Using Safe Fire and Forget, Then Prefer Blazor Lifecycle Tasks

1. In **News.razor.cs**, delete the `async void RefreshOnInitialized()` method.
2. In `OnInitialized()`, replace `RefreshOnInitialized()` with `RefreshAsync(_disposeCancellationTokenSource.Token)`.
3. Append `.SafeFireAndForget(ex => Trace.WriteLine(ex))`:

```cs
protected override void OnInitialized()
{
    IsListRefreshing = true;

    //ToDo Refactor
    RefreshAsync(_disposeCancellationTokenSource.Token).SafeFireAndForget(ex => Trace.WriteLine(ex));
}
```

> **Note:** `.SafeFireAndForget()` is useful when an API truly cannot return `Task`. It observes exceptions instead of silently losing them.

4. For this Blazor lifecycle method, prefer returning `Task` instead of fire-and-forget. Replace `OnInitialized()` with `OnInitializedAsync()`:

```cs
protected override async Task OnInitializedAsync()
{
    IsListRefreshing = true;
    await RefreshAsync(_disposeCancellationTokenSource.Token);
}
```

> **Note:** Blazor lifecycle methods such as `OnInitializedAsync()` are designed for asynchronous work. Returning `Task` lets Blazor await the operation and render the component at the correct times.

## 4. Forwarding Cancellation Tokens

1. In `RefreshAsync(CancellationToken token)`, scroll down to the next `// ToDo Refactor` comment:

```cs
// ToDo Refactor
var minimumRefreshTimeTask = Task.Delay(TimeSpan.FromSeconds(2));
```

2. Forward the `CancellationToken` to `Task.Delay`:

```cs
var minimumRefreshTimeTask = Task.Delay(TimeSpan.FromSeconds(2), token);
```

> **Note:** Forwarding cancellation tokens lets Blazor stop work when a component is disposed, when a user navigates away, or when the caller cancels an operation.

## 5. Using `.ConfigureAwait()`

1. In `RefreshAsync(CancellationToken token)`, scroll down to the next `// ToDo Refactor` comment:

```cs
// ToDo Refactor
var topStoriesList = await GetTopStories(token, StoriesConstants.NumberOfStories);
```

2. Append `.ConfigureAwait(false)`:

```cs
var topStoriesList = await GetTopStories(token, StoriesConstants.NumberOfStories).ConfigureAwait(false);
```

> **Note:** `.ConfigureAwait(false)` tells .NET that the continuation does not need the captured synchronization context. In Blazor component code, update UI state through `InvokeAsync(...)` after using `.ConfigureAwait(false)`.
>
> **Note:** .NET 8 debuted the enum `ConfigureAwaitOptions`, introducing the flags `ConfigureAwaitOptions.None`, `ConfigureAwaitOptions.ContinueOnCapturedContext`, `ConfigureAwaitOptions.SuppressThrowing`, and `ConfigureAwaitOptions.ForceYielding`.

## 6. Avoiding `.Wait()` and `.Result`

1. In `RefreshAsync(CancellationToken token)`, scroll down to the next `// ToDo Refactor` comment:

```cs
// ToDo Refactor
minimumRefreshTimeTask.Wait();
```

2. Replace `.Wait()` with `await`:

```cs
await minimumRefreshTimeTask.ConfigureAwait(false);
```

> **Note:** `.Wait()` and `.Result` block the current thread. In UI frameworks and server request paths, blocking can cause thread starvation, deadlocks, and poor responsiveness.

## 7. Use `IAsyncEnumerable` to Stream Data

1. In **News.razor.cs**, above `GetTopStories(CancellationToken, int)`, scroll down to the next `// ToDo Refactor` comment:

```cs
// ToDo Refactor
async Task<IReadOnlyList<StoryModel>> GetTopStories(CancellationToken token, int storyCount = int.MaxValue)
```

2. Replace `GetTopStories(CancellationToken, int)` with an `IAsyncEnumerable<StoryModel>`:

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

> **Note:** `IAsyncEnumerable` is read using an `await foreach` loop.
>
> **Note:** `[EnumeratorCancellation]` tells .NET to associate the supplied `CancellationToken` with asynchronous enumeration.

3. Update `RefreshAsync(CancellationToken token)` to read the stream and marshal UI updates through Blazor's renderer:

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
        await InvokeAsync(() => RefreshErrorMessage = e.ToString());
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

## 8. Returning `Task`

1. In **News.razor.cs**, above `GetStory(long, CancellationToken)`, scroll down to the next `//ToDo Refactor` comment:

```cs
//ToDo Refactor
async Task<StoryModel> GetStory(long storyId, CancellationToken token)
```

2. Return the `Task` directly:

```cs
Task<StoryModel> GetStory(long storyId, CancellationToken token) => HackerNewsApiService.GetStory(storyId, token);
```

> **Note:** Returning a `Task` directly avoids an unnecessary async state machine when the method does not need its own `await`.

## 9. Using `ValueTask`

1. In **News.razor.cs**, above `GetTopStoryIDs(CancellationToken)`, scroll down to the next `//ToDo Refactor` comment.
2. Update `GetTopStoryIDs(CancellationToken)` to return `ValueTask`:

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

> **Note:** `ValueTask` can avoid a heap allocation when the hot path completes synchronously.
>
> **Note:** Use `ValueTask` sparingly. It is appropriate here because cached story IDs can be returned synchronously without awaiting network I/O.

## 10. Run the Completed Blazor App

1. Navigate to **BecomeAnExpertWithAsyncAwait/2. Correcting Common Async Await Mistakes/2. Finish**.
2. Run the completed app:

```console
dotnet run --project HackerNews/HackerNews.csproj
```

3. Open the localhost URL printed by `dotnet run`.
