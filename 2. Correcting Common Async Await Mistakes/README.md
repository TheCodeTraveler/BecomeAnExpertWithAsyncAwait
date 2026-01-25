# Correcting Common Async Await Mistakes

In this section we will correct common async/await mistakes using `HackerNews.sln`.

## 1. Open **HackerNews.slnx** in IDE

1. Using File Explorer (Windows) / Finder (macOS), navigate to **BecomeAnExpertWithAsyncAwait/2. Correcting Common Async Await Mistakes/1. Start**
2. In the **1. Start** folder, open **HackerNews.slnx** in your IDE (Visual Studio on Windows or Jet Brains Rider on macOS)

<img width="1032" height="674" alt="Screenshot 2026-01-22 at 2 51 36 PM" src="https://github.com/user-attachments/assets/8a714e01-2bb6-40cc-83ff-5b389566499b" />

## 2. Using Async Void

1. In your IDE, open the file **/HackerNews/ViewModels/NewsViewModel**
1. In **NewsViewModel**, in the Constructor, scroll down to the first `// ToDo Refactor` comment:

> //ToDo Refactor
> 
> Refresh(CancellationToken.None); // <-- This `async Task` method is not being awaited

3. In **NewsViewModel**, below the constructor, copy/paste the following `async void Refresh()` method below the constructor:

```cs
public NewsViewModel(IDispatcher dispatcher, HackerNewsAPIService hackerNewsApiService) : base(dispatcher)
{
    _hackerNewsApiService = hackerNewsApiService;

    //ToDo Refactor
    Refresh(CancellationToken.None);
}

async void Refresh()
{
    await Refresh(CancellationToken.None);
}
```

4. In **NewsViewModel**, in the constructor, use the newly created `async void` method:

```cs
public NewsViewModel(IDispatcher dispatcher, HackerNewsAPIService hackerNewsApiService) : base(dispatcher)
{
    _hackerNewsApiService = hackerNewsApiService;

    //ToDo Refactor
    Refresh();
}

async void Refresh()
{
    await Refresh(CancellationToken.None);
}
```

> **Note:** Is it dangerous to use an `async void` method? Let's discuss!

## 3. Using Safe Fire and Forget

1. In **NewsViewModel**, below the constructor, delete the `async void Refresh()` method:

```cs
public NewsViewModel(IDispatcher dispatcher, HackerNewsAPIService hackerNewsApiService) : base(dispatcher)
{
    _hackerNewsApiService = hackerNewsApiService;

    //ToDo Refactor
    Refresh();
}
```

2. In **NewsViewModel**, in the constructor, replace `Refresh()` with `Refresh(CancellationToken.None)`:

```cs
public NewsViewModel(IDispatcher dispatcher, HackerNewsAPIService hackerNewsApiService) : base(dispatcher)
{
    _hackerNewsApiService = hackerNewsApiService;

    //ToDo Refactor
    Refresh(CancellationToken.None);
}
```

3. In **NewsViewModel**, in the constructor, append `.SafeFireAndForget()` to the `Refresh(CancellationToken.None)` method:

```cs
public NewsViewModel(IDispatcher dispatcher, HackerNewsAPIService hackerNewsApiService) : base(dispatcher)
{
    _hackerNewsApiService = hackerNewsApiService;

    //ToDo Refactor
    Refresh(CancellationToken.None).SafeFireAndForget(ex => Trace.WriteLine(ex));
}
```

> **Note:** What is `.SafeFireAndForget()`? Let's discuss!

## 4. Forwarding Cancellation Tokens

1. In **NewsViewModel**, in the `Task Refresh(CancellationToken)` method, scroll down to the next `// ToDo Refactor`
> // ToDo Refactor
> 
> var minimumRefreshTimeTask = Task.Delay(TimeSpan.FromSeconds(2), token);

2. In the `Task Refresh(CancellationToken)` method, note the warning caused by `Task.Delay(TimeSpan.FromSeconds(2))`

<img width="1896" height="532" alt="Screenshot 2026-01-22 at 3 52 20 PM" src="https://github.com/user-attachments/assets/31135b1b-2bdc-4e5b-8de4-18dd30298a96" />

> **Note**: Why is it important to forward a `CancellationToken`? Let's discuss! 

3. In the `.Task Refresh(CancellationToken)` method, forward the `CancellationToken` to the `Task.Delay(TimeSpan.FromSeconds(2))` method:

```cs
var minimumRefreshTimeTask = Task.Delay(TimeSpan.FromSeconds(2), token);
```
> **Note:** Fun Fact! You can capture a `Task` as a variable and `await` it later.

## 5. Using `.ConfigureAwait()`

1. In **NewsViewModel**, in the **Task Refresh(CancellationToken)** method, scroll down to the next `// ToDo Refactor`: 
> // ToDo Refactor
> 
> var topStoriesList = await GetTopStories(token, StoriesConstants.NumberOfStories);

2. In the **NewsViewModel**, in the **Task Refresh(CancellationToken)** method, append to `await GetTopStories(token, StoriesConstants.NumberOfStories)` the extension menthod `.ConfigureAwait(false)`

```cs
var topStoriesList = await GetTopStories(token, StoriesConstants.NumberOfStories).ConfigureAwait(false);
```
> **Note:** `.ConfigureAwait(false)` tells the .NET runtime to continue on any background thread once the await'd `Task` has completed

> **Note:** .NET 8 debuted the enum `ConfigureAwaitOptions`, introducing 4 new Flags we can pass into `.ConfigureAwait()`: `ConfigureAwaitOptions.None`, `ConfigureAwaitOptions.ContinueOnCapturedContext`, `ConfigureAwaitOptions.SuppressThrowing` and `ConfigureAwaitOptions.ForceYielding`

## 6. Avoiding `.Wait()` and `.Result`
1. In **NewsViewModel**, in the **async Task Refresh(CancellationToken token)** method, scroll down to the next `// ToDo Refactor`: 
> // ToDo Refactor
> 
> minimumRefreshTimeTask.Wait();

2. In **NewsViewModel**, in the `async Task Refresh(CancellationToken token)` method, replace `minimumRefreshTimeTask.Wait();` with the following code:

```cs
await minimumRefreshTimeTask.ConfigureAwait(false);
```

## 7. Use `IAsyncEnumerable` to Stream Data
1. In **NewsViewModel**, above the **Task<IReadOnlyList<StoryModel>> GetTopStories(CancellationToken, int)** method, scroll down to the next **// ToDo Refactor**:
> // ToDo Refactor
>
> async Task<IReadOnlyList<StoryModel>> GetTopStories(CancellationToken token, int storyCount = int.MaxValue)

2. In **NewsViewModel**, replace the **Task<IReadOnlyList<StoryModel>> GetTopStories(CancellationToken, int)** method:

```cs
async IAsyncEnumerable<StoryModel> GetTopStories(int storyCount, [EnumeratorCancellation] CancellationToken token)
{
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(storyCount);

    var topStoryIds = await GetTopStoryIDs(token).ConfigureAwait(false);

    var getTopStoryTaskList = topStoryIds.Select(id => GetStory(id, token)).ToList();

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
> **Note:** `IAsyncEnumerable` is read using an `await foreach()` loop

> **Note:** `[EnumeratorCancellation]` tells the .NET runtime to check the associated `CancellationToken` on each iteration of the `IAsyncEnumerable`

3. In **NewsViewModel**, scroll up to the red squiggles in the **Task Refresh(CancellationToken)** method
4. Update the **Task Refresh(CancellationToken)** method using the following code:

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
                InsertIntoSortedCollection(TopStoryCollection, (a, b) => b.Score.CompareTo(a.Score), story);
        }
    }
    catch (Exception e)
    {
        PullToRefreshFailed?.Invoke(this, e.ToString());
    }
    finally
    {
        await minimumRefreshTimeTask.ConfigureAwait(false);
        IsListRefreshing = false;
    }
}
```

## 8. Returning `Task`

1. In **NewsViewModel**, above the **Task<StoryModel> GetStory(long, CancellationToken)** method, scroll down to the next `// ToDo Refactor`:
> //ToDo Refactor
>
> async Task<StoryModel> GetStory(long storyId, CancellationToken token)

2. Update the `Task<StoryModel> GetStory(long, CancellationToken)` method as follows:

```cs
	Task<StoryModel> GetStory(long storyId, CancellationToken token)
	{
		return _hackerNewsApiService.GetStory(storyId, token);
	}
```
> **Note:** Returning a `Task` improves performance by avoiding unnecessary thread switching

## 9. Using `ValueTask`

1. In **NewsViewModel**, above the **Task<IReadOnlyList<long>> GetTopStoryIDs(CancellationToken)** method, scroll down to the next `// ToDo Refactor`:
2. Update the the **Task<IReadOnlyList<long>> GetTopStoryIDs(CancellationToken)** method to return `ValueTask`:

```cs
//ToDo Refactor
async ValueTask<IReadOnlyList<long>> GetTopStoryIDs(CancellationToken token)
{
    if (IsDataRecent(TimeSpan.FromHours(1)))
        return TopStoryCollection.Select(x => x.Id).ToList();

    try
    {
        return await _hackerNewsApiService.GetTopStoryIDs(token);
    }
    catch (Exception e)
    {
        Trace.WriteLine(e.Message);
        throw;
    }
}
```
> **Note:** `ValueTask` is more performant than `Task` because it is a value-type which is initialized on the Stack whereas `Task` is a reference-type that is initialized on the Heap
>
> **Note:** Use `ValueTask` when the hot-path of the method does not require the `await` keyword
