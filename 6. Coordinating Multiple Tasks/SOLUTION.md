# Solution Walkthrough: Coordinating Multiple Tasks

Use this during the guided walkthrough after the challenge and group review in [README.md](README.md).

The completed project is **2. Finish/ProductDetails**. Compare your decisions with the finished Blazor sample as we rebuild the solution step by step. Only one source file you will edit differs between **1. Start** and **2. Finish**: `Components/Pages/Product.razor.cs`. The only other difference is the port in `Properties/launchSettings.json`, so both apps can run side by side, with Start on 5009 and Finish on 5010. The five services, the models, and the Razor markup are already correct, which is worth saying out loud. Nothing about this problem is fixed by changing the backend.

## 1. Read the Starter Method Again

The starter awaits each service on the same line that starts it:

```cs
// ToDo Refactor: these five services do not depend on each other, but
// each await waits for the previous one to finish. The page costs the
// sum of every latency instead of the slowest one.
var inventory = await InventoryService.GetInventoryAsync(_sku, CancellationToken.None).ConfigureAwait(false);
SetPanel("Inventory", "ready", $"{inventory.InStock} in stock at {inventory.Warehouse}", stopwatch.Elapsed);

var pricing = await PricingService.GetPricingAsync(_sku, CancellationToken.None).ConfigureAwait(false);
SetPanel("Pricing", "ready", $"{pricing.YourPrice:C} (list {pricing.ListPrice:C})", stopwatch.Elapsed);
```

`await` does not start work. `InventoryService.GetInventoryAsync(...)` starts the work, and `await` only says "I have nothing else to do until this finishes." Putting both on one line means the pricing call cannot start until inventory has answered, and the reviews call cannot start until pricing has answered. The page costs 700 + 900 + 1200 + 600 + 800 milliseconds, which is the 4.2 seconds on the timing tile. The slowest single service is reviews at 1.2 seconds, so 3 of those 4.2 seconds are spent waiting for permission to begin.

The second problem is at the bottom of the same method:

```cs
	// ToDo Refactor: this service is down. Every call shares one try block,
	// so its failure is the whole page's failure. Move it above Reviews and
	// two more panels go blank. One flaky service should degrade one panel.
	var recommendations = await RecommendationsService.GetRecommendationsAsync(_sku, CancellationToken.None).ConfigureAwait(false);
	SetPanel("Recommendations", "ready", string.Join(", ", recommendations.AlsoBought), stopwatch.Elapsed);
}
catch (HttpRequestException e)
{
	PageError = e.Message;
}
```

One `try` block covers all five calls. When the recommendations service throws, control jumps straight to the `catch`, the page sets a page-level error, and the last `SetPanel(...)` never runs at all, so the Recommendations card is left saying `waiting`. Move that call above the reviews call and two more panels go blank. How much of the page dies has nothing to do with the failure and everything to do with where the call sits in the block.

## 2. Start Every Call Before You Await Any of Them

Replace the five sequential awaits with five calls that are all started before anything is awaited:

```cs
// Start every call before awaiting any of them. From here the page
// costs the slowest service, not the sum of all five.
var panelTasks = new List<Task>
{
	TrackPanelAsync("Inventory", InventoryService.GetInventoryAsync(_sku, CancellationToken.None),
		static inventory => $"{inventory.InStock} in stock at {inventory.Warehouse}", stopwatch),

	TrackPanelAsync("Pricing", PricingService.GetPricingAsync(_sku, CancellationToken.None),
		static pricing => $"{pricing.YourPrice:C} (list {pricing.ListPrice:C})", stopwatch),

	TrackPanelAsync("Reviews", ReviewsService.GetReviewsAsync(_sku, CancellationToken.None),
		static reviews => $"{reviews.AverageRating:F1} stars from {reviews.ReviewCount:N0} reviews", stopwatch),

	TrackPanelAsync("Shipping", ShippingService.GetShippingAsync(_sku, CancellationToken.None),
		static shipping => $"{shipping.Carrier}, arrives {shipping.EstimatedArrival:MMM d}", stopwatch),

	TrackPanelAsync("Recommendations", RecommendationsService.GetRecommendationsAsync(_sku, CancellationToken.None),
		static recommendations => string.Join(", ", recommendations.AlsoBought), stopwatch),
};
```

Each entry invokes its service immediately, which is what puts all five delays in flight at the same moment. `TrackPanelAsync(...)` is an `async` method, so it returns to the caller at its first `await`, and the whole list is built about as fast as the CPU can build it. From the closing brace onward, the page owes the slowest service, not the sum of all five.

Every call still passes `CancellationToken.None`, exactly as the starter did. Nothing about coordinating these calls changes how they are cancelled, and keeping the argument there means the call sites are ready the day you have a real token to pass.

Notice that `panelTasks` is a `List<Task>` and not a `List<Task<T>>`. Each service returns a different type, so there is no single result type to collect. Each wrapper records its own panel, and the list exists only so the page has something to wait on.

## 3. Wrap Each Call So One Failure Degrades One Panel

The wrapper is where the failure is contained:

```cs
// Wraps one backend call so a single failing service degrades one panel
// instead of taking down the whole page.
async Task TrackPanelAsync<T>(string name, Task<T> serviceCall, Func<T, string> describe, Stopwatch stopwatch)
{
	try
	{
		var result = await serviceCall.ConfigureAwait(false);

		SetPanel(name, "ready", describe(result), stopwatch.Elapsed);
	}
	catch (HttpRequestException e)
	{
		SetPanel(name, "failed", e.Message, stopwatch.Elapsed);
	}
}
```

`TrackPanelAsync<T>` takes the panel name, the already-started `Task<T>`, and a `Func<T, string>` that knows how to describe that service's result. The `try` covers exactly one service call, so a failure can only reach one `SetPanel(...)`. A successful call marks the panel `"ready"`; a failed call marks it `"failed"` and puts the service's message on the card, which is what makes the Recommendations card render in the failure style.

Catch the exception you actually expect. `HttpRequestException` is what a failing HTTP dependency throws. A bare `catch (Exception)` here would also swallow programming errors you want to see.

Because every failure is handled inside the wrapper, the `Task` the wrapper returns always completes successfully. That matters for the next step.

## 4. Stream the Completions With Task.WhenEach

```cs
// Task.WhenEach streams each task as it finishes, so every panel paints
// the moment its own service answers instead of waiting for the slowest.
await foreach (var finishedPanel in Task.WhenEach(panelTasks))
{
	// Each task already recorded its own panel and swallowed its own
	// failure, so awaiting here only observes completion.
	await finishedPanel.ConfigureAwait(false);

	await InvokeAsync(StateHasChanged).ConfigureAwait(false);
}
```

`Task.WhenEach` was added in .NET 9. It returns an `IAsyncEnumerable<Task>` that hands you each task the moment that task completes, so `await foreach` runs its body five times at 0.6s, 0.7s, 0.8s, 0.9s, and 1.2s. In practice that is finishing order rather than the order you built the list in, and the documentation deliberately stops short of guaranteeing an exact order for tasks that finish at nearly the same instant. Depend on getting each task as soon as it is done, not on the sequence. There is no list to trim and no bookkeeping to forget.

`Task.WhenEach` yields the `Task`, not the result, which is why the body awaits `finishedPanel`. Step 3 already caught the only failure the wrapper can produce, so that `await` observes completion and nothing more.

`InvokeAsync(StateHasChanged)` is what turns each completion into a repaint. Every `await` in this method uses `ConfigureAwait(false)`, so the continuation may not be on Blazor's renderer. `SetPanel(...)` only overwrites one entry in the component's own list, and each panel's write happens before its own task completes, so the render that follows always sees it. What has to go back through `InvokeAsync(...)` is the render itself, which is why `StateHasChanged` is wrapped and `SetPanel(...)` is not.

## 5. Stop the Page From Owning a Panel's Failure

Every failure now belongs to a panel, so the page-level error has nothing left to report:

```cs
// Every panel handles its own failure, so the page itself never fails
public string? PageError => null;
```

`Product.razor` still renders the warning banner when `PageError` is not null, and that markup is unchanged. The property simply never has anything to say now, so the banner never appears.

Two more things fall away with it. The reset at the top of the method loses its `PageError = null;` line:

```cs
protected async Task LoadProductAsync()
{
	IsLoading = true;
	TotalSeconds = null;
	ResetPanels();

	await InvokeAsync(StateHasChanged).ConfigureAwait(false);

	var stopwatch = Stopwatch.StartNew();
```

And the `finally` block is gone, because there is no `try` left for it to belong to:

```cs
	stopwatch.Stop();
	TotalSeconds = stopwatch.Elapsed.TotalSeconds;
	IsLoading = false;

	await InvokeAsync(StateHasChanged).ConfigureAwait(false);
}
```

Note what the `finally` was buying you. `TrackPanelAsync` only catches `HttpRequestException`, so anything else still escapes the `await foreach` loop and leaves `IsLoading` stuck at `true` with the button disabled for the life of the circuit. In production code, either keep a `try`/`finally` around the loop purely to reset `IsLoading`, or widen the wrapper's catch so no wrapper task can ever fault.

The stopwatch stops after the last panel has painted, so the timing tile reports the real end-to-end cost of the page.

## 6. What Task.WhenAll and Task.WhenAny Would Have Done

These two are not what **2. Finish** uses, but both are correct answers to the timing half of the challenge, and you should be able to write either one on demand.

`Task.WhenAll` gives you the same 1.2 seconds in a single line:

```cs
await Task.WhenAll(panelTasks).ConfigureAwait(false);
```

The difference is when the user sees anything. `Task.WhenAll` completes once, at 1.2 seconds, so all five cards appear together unless each wrapper repaints itself as it finishes. If you kept the wrappers and moved `InvokeAsync(StateHasChanged)` into `TrackPanelAsync`, `Task.WhenAll` passes every acceptance check in the challenge.

`Task.WhenAll` is also the only one of the three that collects failures for you. If you had not wrapped each call, `await Task.WhenAll(...)` would rethrow only one exception, and it is the first faulted task in the order you passed them in, not the first one to fail. The rest are still there: hold on to the task, and after it faults, its `Exception` property is an `AggregateException` whose `InnerExceptions` holds every failure.

`Task.WhenAny` is the manual version of `Task.WhenEach`:

```cs
var pending = new List<Task>(panelTasks);

while (pending.Count > 0)
{
	var finished = await Task.WhenAny(pending).ConfigureAwait(false);

	pending.Remove(finished);

	await finished.ConfigureAwait(false);

	await InvokeAsync(StateHasChanged).ConfigureAwait(false);
}
```

`pending.Remove(finished)` is the whole trick. `Task.WhenAny` hands back the task that finished, and a finished task stays finished, so leaving it in the list means the next call returns the same task again and the loop never ends. Awaiting `finished` after you remove it does the same job as step 4: it observes the completion so nothing is silently dropped if a wrapper ever does throw. `Task.WhenEach` exists so you never have to remember any of this.

## 7. Check It in the Browser

Run the finished app from **2. Finish**, which is configured for port 5010 so it can run beside the starter:

```console
dotnet run --project ProductDetails/ProductDetails.csproj
```

Open [http://localhost:5010](http://localhost:5010) and confirm each of these:

1. Shipping lands first at 0.6s.
2. Inventory follows at 0.7s.
3. Recommendations fails at 0.8s, and its card turns red with the 503 message on it.
4. Pricing lands at 0.9s.
5. Reviews lands last at 1.2s.
6. The total page load tile reads 1.2s.
7. There is no yellow banner across the top of the page.

Same five services, same latencies, same broken dependency. The only thing that changed is when you awaited and where you caught.

## 8. Compare Against Finish

Compare your implementation with the completed file:

[2. Finish/ProductDetails/Components/Pages/Product.razor.cs](2.%20Finish/ProductDetails/Components/Pages/Product.razor.cs)

Focus on the reasons behind each change, not only whether your code is textually identical.
