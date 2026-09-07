# Coordinating Multiple Tasks

In this section, you will refactor the Blazor **ProductDetails** starter app so a product page stops paying for its backend services one at a time. **ProductDetails** renders a single product page that needs five independent backend services: inventory, pricing, reviews, shipping, and recommendations. Nothing on the page depends on anything else on the page, but the code awaits each service before it starts the next one, so the page costs the sum of every latency instead of the cost of the slowest one. The recommendations engine is also down, and because all five calls share one `try` block, one service failing is the whole page failing.

The **1. Start** folder contains the intentionally imperfect code you will edit. The **2. Finish** folder contains the completed Blazor version.

## 1. Open the Starter Project

1. Using File Explorer on Windows or Finder on macOS, navigate to **BecomeAnExpertWithAsyncAwait/6. Coordinating Multiple Tasks/1. Start**.
2. Open **ProductDetails.slnx** in your IDE.
3. Open **ProductDetails/Components/Pages/Product.razor.cs**.
4. Build the project once so you can confirm your environment is ready.
5. Run the project and leave it running while you read the code.

```console
dotnet build ProductDetails.slnx
```

```console
dotnet run --project ProductDetails/ProductDetails.csproj
```

The app runs at [http://localhost:5009](http://localhost:5009).

## 2. Inspect the Starting Code

1. Open **ProductDetails/Services/BackendServices.cs** and note what each service costs: inventory 700ms, pricing 900ms, reviews 1200ms, shipping 600ms, and recommendations 800ms.
2. Notice that `RecommendationsService.GetRecommendationsAsync(...)` always throws `HttpRequestException`. The flaky service is always the one you depend on.
3. Open **ProductDetails/Components/Pages/Product.razor.cs** and find each `// ToDo Refactor` comment.
4. Read `LoadProductAsync()` from top to bottom before you change anything.
5. Open **ProductDetails/Components/Pages/Product.razor** and see how one card renders in each of its four states: `waiting`, `ready`, `failed`, and `skipped`.

Now watch the app in the browser. The page loads once when it opens, and the **Load product page** button runs the same code again:

1. Nothing happens for about four seconds. The button spins and all five cards say `waiting`.
2. Four cards then appear at the same instant, at the very end, instead of appearing as their services answer.
3. The total page load tile reads **4.2s**.
4. The per-card timings read 0.7s, 1.6s, 2.8s, and 3.4s. Those are running totals, and they add up to the sum of every service call.
5. The Recommendations card shows `--` and the words `never requested` with a red left edge. Its service was next in line and never got called.
6. A yellow banner sits across the top of the page: "The page load stopped. Recommendations service returned 503 Service Unavailable. Every panel below it was never requested."

Pay attention to these clues:

1. Every `await` in `LoadProductAsync()` sits on the same line that starts the call, so the next service cannot start until the previous one has answered.
2. The five services are injected separately and share no state, so nothing about the data forces this order.
3. The slowest single service takes 1.2 seconds, and 1.2 is a lot smaller than 4.2.
4. The method has five `SetPanel(...)` calls but only four of them are ever reached, because the call above the fifth one throws. `InvokeAsync(StateHasChanged)` only runs before the work starts and after all of it ends, so nothing repaints in between.
5. One `catch (HttpRequestException)` covers all five calls, so the first failure skips everything after it.
6. Move the recommendations call above the reviews call and two more cards go blank. How much of the page dies is decided by the shape of the code, not by the failure.

## 3. Challenge: Coordinate the Product Page

Recommended time: 20 minutes.

> **Note:** Please avoid letting AI Agents solve the challenges for you. You're smart. You got this. Use them to understand the existing code, clarify how `Task.WhenAll`, `Task.WhenAny`, and `Task.WhenEach` differ, interpret errors, and ask questions that help you decide what to change. The goal is to practice the reasoning yourself.

Refactor **Product.razor.cs** so the five backend calls are all in flight at the same time and a single failing service can only damage its own card.

Requirements:

1. Start all five backend calls before you await any of them.
2. Coordinate them with `Task.WhenAll`, `Task.WhenAny`, or `Task.WhenEach`.
3. Keep the total page load at the cost of the slowest service, not the sum of all five.
4. Give each call its own error handling so a failure is recorded against one card.
5. Set the failing card's status to `"failed"` and put the service's message on that card.
6. Repaint the page as each service answers instead of once at the end.
7. Keep each card's timing the elapsed time at which that service answered.
8. Stop reporting a single card's failure as a failure of the whole page.
9. Keep passing a `CancellationToken` to every service call. The starter passes `CancellationToken.None`, and so does the finished version, so keep it there and the call sites stay ready for a real token.
10. Keep using `ConfigureAwait(false)`, and keep marshaling each repaint back through `InvokeAsync(StateHasChanged)`.

Acceptance checks:

1. **ProductDetails.slnx** builds.
2. The total page load tile reads about **1.2s** instead of 4.2s.
3. Cards fill in one at a time as their services answer, rather than all appearing together at the end.
4. The per-card timings read roughly 0.6s, 0.7s, 0.8s, 0.9s, and 1.2s. No card reports 1.6s, 2.8s, or 3.4s.
5. The recommendations service still fails, because you cannot fix somebody else's 503. Its card now reads `Unavailable` with the reason underneath and its own timing, in the failure style with a red left edge. Every other panel is unaffected.
6. The yellow banner across the top of the page is gone.
7. Clicking **Load product page** again gives the same result every time.
8. Your code is ready to compare with **2. Finish/ProductDetails**.

## 4. Review the Solution

After you have attempted the challenge, pause here for group review.

We will compare approaches, talk about when `Task.WhenAll` is enough and when streaming each completion is worth the extra code, and answer questions before opening [SOLUTION.md](SOLUTION.md) together.

The solution walkthrough shows the order to make each change and points you to the completed code in **2. Finish/ProductDetails**.
