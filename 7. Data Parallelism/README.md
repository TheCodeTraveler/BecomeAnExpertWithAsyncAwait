# Data Parallelism

ImportPortal runs the nightly order import. Every night it reads the day's uploaded order file, scores each row for risk, enriches each row with the customer's tier from an internal API, and summarizes the results by region. It has been in production for a year. Last week someone noticed that not one row is coming back with a customer tier, and that the import has been reporting success the whole time. In this section you will find out why, and you will put the rest of the processors on the machine to work while you are in there.

The **1. Start** folder contains the intentionally imperfect code you will edit. The **2. Finish** folder contains the completed Blazor version.

## 1. Open the Starter Project

1. Using File Explorer on Windows or Finder on macOS, navigate to **BecomeAnExpertWithAsyncAwait/7. Data Parallelism/1. Start**.
2. Open **ImportPortal.slnx** in your IDE.
3. Build the project once so you can confirm your environment is ready. The starter builds with zero warnings. That is part of the lesson.
4. Run the project and open the app in your browser.

The starter app runs at [http://localhost:5011](http://localhost:5011). The finished app runs at [http://localhost:5012](http://localhost:5012), so you can run both at the same time and compare them later.

The app opens on the **Import page**, with the workshop guide docked beside it. Every step in the guide is one stage of the nightly import, and every time the app starts it checks your code against them. Right now the guide says it stopped at Step 1, and the terminal running the app shows the same result, with a hint.

## 2. Inspect the Starting Code

1. Open **ImportPortal/Services/ImportService.cs** and find each `// ToDo Refactor (Step N)` comment. The step number tells you which step checks that line. All three stages of the import live in `RunImportAsync(int, CancellationToken)`.
2. Read `ScoreRisk(OrderRow)` at the bottom of the same file. It is deliberately expensive, it is pure CPU work, and it never reads a value another row wrote.
3. Open **ImportPortal/Services/CustomerApiService.cs**. `GetCustomerTierAsync(string, CancellationToken)` waits 10 milliseconds per call. That is I/O, not CPU. The service also counts how many calls are in flight, which is how the workshop steps see what the import really waited for. You do not need to change it.
4. Open **ImportPortal/Models/OrderRow.cs** and note the two properties that are filled in later: `RiskScore` by the validation stage, and `CustomerTier` by the enrichment stage.
5. Open **ImportPortal/Components/Pages/Import.razor** and **Import.razor.cs** to see where the report on screen comes from. You do not need to change either file.
6. Open the **ImportPortal/Steps** folder. Each `Step*.cs` file runs your `ImportService` on fresh services, with comments that explain what each expected result checks and why. These files are your guide.
7. Skim **ImportPortal/Verification** and **ImportPortal/Components/Layout/WorkshopGuide.razor**. They are the workshop plumbing that runs the steps and draws the guide beside the app. You do not need to change them.

Now switch back to the browser and click **Run import** on the **Import page** beside the guide. The tile in the header tells you how many processors are available. Keep that number in mind while you read the four cards under **Import stages**:

1. **Validated** reads 4,000 of 4,000 in about 2.12 seconds. That answer is correct, and one thread produced all of it.
2. **Enriched** reads 0 of 4,000 in about 0.01 seconds, and the card is red. Not a single customer tier came back.
3. **Report** shows how long the regional grouping took, on one thread, after every row was already in memory.
4. **Total** is the end to end time, and almost all of it belongs to the first card. Write that number down as your baseline.

Then scroll to the **Regional summary** panel. All four regions show 1,000 orders each, but those counts come straight from the file and would look right even if nothing had run. The average risk figures are the ones that prove validation really happened.

The numbers quoted in this section were measured on a machine that reports 16 processors. Yours will be different. The shape of the problem will not be.

Pay attention to these clues:

1. The validation loop is a plain `foreach`, and nothing in its body depends on the row before it.
2. The enrichment stage calls `Parallel.ForEach` with an `async` lambda, and the compiler is perfectly happy with it.
3. The body `Parallel.ForEach` takes here is an `Action<T>`, and no overload of it accepts a `Task`-returning body. Ask yourself what an `async` lambda turns into when it is bound to a delegate that returns `void`.
4. The stage that never touches the network reports the longest time, and the stage that should be waiting on 4,000 calls at 10 milliseconds each reports 0.01 seconds.
5. `RunImportAsync` is an `async` method whose only `await` is `await Task.Yield()`.
6. The method already receives a `CancellationToken`, and nothing except the customer API call ever uses it.
7. The regional summary runs as a single-threaded LINQ query after every row is already in memory.

The app walks you through the challenge one step at a time:

1. Step 1 scores every row on every core, Step 2 enriches every row and waits for it, and Step 3 summarizes with PLINQ and stops when the import is cancelled.
2. A step unlocks only after the step before it passes. Each step in the guide tells the story behind the bug, how to see it on the Import page, which file to change, and a task list for that step. If you get stuck, it has clues.
3. Each step runs your `ImportService` on fresh services and measures what it really did: how many cores validation kept busy, how many customer API calls were still in flight when the import returned, and how much CPU time a cancelled import burned. It shows a checklist of every expected result next to what actually happened. Every result that does not match comes with a hint, and the same hint is printed in the terminal running the app.
4. Every time the app starts, it checks your code against every step, so the workshop guide always reflects the code you have now.
5. Stop and run the app again after each change. If your IDE applied the change with Hot Reload, click **Run every step** in the workshop guide instead.

## 3. Challenge: Fix the Nightly Import

Recommended time: 25 minutes.

> **Note:** Please avoid letting AI Agents solve the challenges for you. You're smart. You got this. Use them to understand the existing code, clarify parallel programming concepts, interpret errors, and ask questions that help you decide what to change. The goal is to practice the reasoning yourself.

Refactor **Services/ImportService.cs** so all three stages use the data parallelism primitive that fits them. Do not change **CustomerApiService.cs**, **OrderFileService.cs**, **OrderRow.cs**, or the Import page.

Requirements, in the order the steps check them:

1. Replace the sequential validation `foreach` so every processor scores rows at the same time.
2. Leave `ScoreRisk(OrderRow)` exactly as it is. The goal is to run it on more threads, not to make it cheaper.
3. Fix the enrichment stage so all 4,000 rows really are enriched, and so `RunImportAsync` does not return until they are.
4. Use the `Parallel` overload that is built for asynchronous bodies, and forward the `CancellationToken` that overload hands to your body.
5. Bound the enrichment stage so you do not fire 4,000 calls at the customer API at once. Pick a limit and be ready to explain the number you picked.
6. Turn the regional summary into a PLINQ query that honors the cancellation token.
7. Set `MaxDegreeOfParallelism` and `CancellationToken` through `ParallelOptions`, using the token that `RunImportAsync` already receives, so a cancelled import stops before it scores a single row. The CPU stage and the I/O stage do not have to agree on the same ceiling.
8. Let the `OperationCanceledException` reach the caller. A cancelled import has no report to return.
9. Do not add a shared counter, list, or dictionary that parallel bodies write to. If you find yourself wanting one, stop and work out why you do not need it here.

Acceptance checks:

1. **ImportPortal.slnx** builds.
2. The workshop guide shows 3 of 3 steps pass after the app starts.
3. On the Import page at [http://localhost:5011](http://localhost:5011), **Validated** still reads 4,000 of 4,000, and its time is a fraction of the validation time you wrote down before you changed anything.
4. **Enriched** reads 4,000 of 4,000, and its card is green instead of red.
5. The **Enriched** time is now measured in seconds rather than hundredths of a second, because the app is finally making 4,000 real API calls. Divide 4,000 by the limit you chose and multiply by the 10 millisecond call time to predict roughly what the card should say, then check it against your prediction.
6. On a machine with several processors, the **Total** card is at or below the total you recorded from the starter, even though the app is now doing thousands of calls it used to skip.
7. **Regional summary** still shows US-CA, US-NY, US-TX, and US-WA with 1,000 orders each, and the revenue for each region is unchanged.
8. Clicking **Run import** several times in a row gives the same counts every time, and no card turns red.
9. Your code is ready to compare with **2. Finish/ImportPortal**.

## 4. Review the Solution

After you have attempted the challenge, pause here for group review.

We will compare approaches, talk about which stage deserved which primitive and what limit you chose for the customer API, and answer questions before opening [SOLUTION.md](SOLUTION.md) together.

The solution walkthrough rebuilds the import one stage at a time and points you to the completed code in **2. Finish/ImportPortal**.
