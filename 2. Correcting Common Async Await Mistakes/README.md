# 1. Correcting Common Async Await Mistakes

In this section we will correct common async/await mistakes using `HackerNews.sln`.

## 1. Open **HackerNews.slnx** in IDE

1. Using File Explorer (Windows) / Finder (macOS), navigate to **BecomeAnExpertWithAsyncAwait/2. Correcting Common Async Await Mistakes/1. Start**
2. In the **1. Start** folder, open **HackerNews.slnx** in your IDE (Visual Studio on Windows or Jet Brains Rider on macOS)

<img width="1032" height="674" alt="Screenshot 2026-01-22 at 2 51 36 PM" src="https://github.com/user-attachments/assets/8a714e01-2bb6-40cc-83ff-5b389566499b" />

## 2a Build/Run the App (macOS)

1. In **Jet Brains Rider**, using the macOS Menu Bar, navigate to **JetBrains Rider -> Settings**

<img width="376" height="302" alt="image" src="https://github.com/user-attachments/assets/f06c3819-fe72-46dc-bd7c-cf9fd38d75a7" />

2. In the Jet Brains Rider **Settings Menu**, on the left-hand menu, select **Plugins**
3. In the **Plugins** window, at the top of the window, select **Marketplace**

<img width="1462" height="1162" alt="Screenshot 2026-01-22 at 3 11 56 PM" src="https://github.com/user-attachments/assets/662c025c-8559-4b1f-ab42-59d706e10f97" />

4. In the **Plugins** window, in the **search bar**, type `Rider Android Support`
5. In the **Plugins** window, in the search results, locate the **Rider Android Support** plugin
6. On the **Rider Android Support** plugin, click **Install**

> **Note:** If **Rider Android Support** is already installed, skip this step

<img width="1462" height="1162" alt="Screenshot 2026-01-22 at 3 14 54 PM" src="https://github.com/user-attachments/assets/ee5fe44f-f405-423a-a9fa-e43477e539f1" />

7. Stand by while the **Rider Android Support** plugin is installed
8. After the **Rider Android Support** has installed, click **Restart IDE**
9. Stand by until Jet Brains Rider restarts
10. After Jet Brains Rider has restarted, open **HackerNews.slnx**
11. In Jet Brains Rider, on the top-right corner of the toolbar, click the **HackerNews** startup project drop-down menu

<img width="633" height="280" alt="image" src="https://github.com/user-attachments/assets/7ef7075c-978a-49f7-b0b8-b09913cec8b0" />

12. In the **HackerNews** startup project drop-down menu, select the Android icon

> **Note**: Alternatively, you may select the macOS or iOS icon if you have [Xcode](https://developer.apple.com/xcode/) installed

13. In Jet Brains Rider, on the top-center of the toolbar, click the Android Device drop-down menu

<img width="1212" height="357" alt="image" src="https://github.com/user-attachments/assets/dc4463ce-5e88-4741-ba66-9683f8a2dfe7" />

14. In the Android device drop-down menu, select an Android simulator targeting Android API 25 or higher

15. In Jet Brains Rider, on the top-right corner of the toolbar, click **Debug**

<img width="417" height="242" alt="image" src="https://github.com/user-attachments/assets/b1af904a-5190-4513-8277-beaa5a1d9592" />

16. Confirm the app succesfully builds, launches, and runs

<img width="737" height="1083" alt="image" src="https://github.com/user-attachments/assets/2d07f935-055e-4e21-b9aa-fb1c54fc2558" />

## 3. Using Async Void

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

## 4. Using Safe Fire and Forget

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
    Refresh(CancellationToken.None).SafeFireAndForget(ex => Trace.WriteLine(ex);
}
```

> **Note:** What is `.SafeFireAndForget()`? Let's discuss!

## 5. Forwarding Cancellation Tokens

1. In **NewsViewModel**, in the `Task Refresh(CancellationToken)` method, scroll down to the next `// ToDo Refactor`
> // ToDo Refactor
> 
> var minimumRefreshTimeTask = Task.Delay(TimeSpan.FromSeconds(2), token);

3. In the `Task Refresh(CancellationToken)` method, note the warning caused by `Task.Delay(TimeSpan.FromSeconds(2))`

<img width="1896" height="532" alt="Screenshot 2026-01-22 at 3 52 20 PM" src="https://github.com/user-attachments/assets/31135b1b-2bdc-4e5b-8de4-18dd30298a96" />

> **Note**: Why is it important to forward a `CancellationToken`? Let's discuss! 

3. In the `.Task Refresh(CancellationToken)` method, forward the `CancellationToken` to the `Task.Delay(TimeSpan.FromSeconds(2))` method:

```cs
var minimumRefreshTimeTask = Task.Delay(TimeSpan.FromSeconds(2), token);
```
> **Note:** Fun Fact! You can capture a `Task` as a variable and `await` it later.

## 6. Using `.ConfigureAwait()`

1. In **NewsViewModel**, in the `Task Refresh(CancellationToken)` method, scroll down to the next `// ToDo Refactor`: 
> // ToDo Refactor
> 
> var topStoriesList = await GetTopStories(token, StoriesConstants.NumberOfStories);

2. In the **NewsViewModel**, in the `Task Refresh(CancellationToken)` method, append to `await GetTopStories(token, StoriesConstants.NumberOfStories)` the extension menthod `.ConfigureAwait(false)`

```cs
var topStoriesList = await GetTopStories(token, StoriesConstants.NumberOfStories).ConfigureAwait(false)
```
> **Note:** `.ConfigureAwait(false)` tells the .NET runtime to continue on any background thread once the await'd `Task` has completed

> **Note:** .NET 8 debuted the enum `ConfigureAwaitOptions`, introducing 4 new Flags we can pass into `.ConfigureAwait()`: `ConfigureAwaitOptions.None`, `ConfigureAwaitOptions.ContinueOnCapturedContext`, `ConfigureAwaitOptions.SuppressThrowing` and `ConfigureAwaitOptions.ForceYielding`

## 7. Avoiding `.Wait()` and `.Result`
1. In **NewsViewModel**, in the `async Task Refresh(CancellationToken token)` method, scroll down to the next `// ToDo Refactor`: 
> // ToDo Refactor
> 
> minimumRefreshTimeTask.Wait();

2. In **NewsViewModel**, in the `async Task Refresh(CancellationToken token)` method, replace `minimumRefreshTimeTask.Wait();` with the following code:

```cs
await minimumRefreshTimeTask.ConfigureAwait(false);
```

## 8. Use `IAsyncEnumerable` to Stream Data
1. In **NewsViewModel**, above the `Task<FrozenSet<StoryModel>> GetTopStories(CancellationToken, int)` method, scroll down to the next `// ToDo Refactor`:
> // ToDo Refactor
>
> async Task<IReadOnlyList<StoryModel>> GetTopStories(CancellationToken token, int storyCount = int.MaxValue)
