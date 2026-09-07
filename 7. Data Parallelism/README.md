# Data Parallelism

ImportPortal runs the nightly order import. Every night it reads the day's uploaded order file, scores each row for risk, enriches each row with the customer's tier from an internal API, and summarizes the results by region. It has been in production for a year. Last week someone noticed that not one row is coming back with a customer tier, and that the import has been reporting success the whole time. In this section you will find out why, and you will put the rest of the processors on the machine to work while you are in there.

The **1. Start** folder contains the intentionally imperfect code you will edit. The **2. Finish** folder contains the completed Blazor version.

## 1. Open the Starter Project

1. Using File Explorer on Windows or Finder on macOS, navigate to **BecomeAnExpertWithAsyncAwait/7. Data Parallelism/1. Start**.
2. Open **ImportPortal.slnx** in your IDE.
3. Build the project once so you can confirm your environment is ready. The starter builds with zero warnings. That is part of the lesson.
4. Run the project and open the app in your browser.

```console
dotnet build ImportPortal.slnx
```

```console
dotnet run --project ImportPortal/ImportPortal.csproj
```

The starter app runs at [http://localhost:5011](http://localhost:5011). The finished app runs at [http://localhost:5012](http://localhost:5012), so you can run both at the same time and compare them later.

## 2. Inspect the Starting Code

1. Open **ImportPortal/Services/ImportService.cs** and find the three `// ToDo Refactor` comments. All three stages of the import live in `RunImportAsync(int, CancellationToken)`.
2. Read `ScoreRisk(OrderRow)` at the bottom of the same file. It is deliberately expensive, it is pure CPU work, and it never reads a value another row wrote.
3. Open **ImportPortal/Services/CustomerApiService.cs**. `GetCustomerTierAsync(string, CancellationToken)` waits 10 milliseconds per call. That is I/O, not CPU.
4. Open **ImportPortal/Models/OrderRow.cs** and note the two properties that are filled in later: `RiskScore` by the validation stage, and `CustomerTier` by the enrichment stage.
5. Open **ImportPortal/Components/Pages/Import.razor** and **Import.razor.cs** to see where the report on screen comes from. You do not need to change either file.

Now run the app and click **Run import**. The tile in the header tells you how many processors are available. Keep that number in mind while you read the four cards under **Import stages**:

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
5. `RunImportAsync` is an `async` method whose only `await` is `await Task.CompletedTask.ConfigureAwait(false)`.
6. The method already receives a `CancellationToken`, and nothing except the customer API call ever uses it.
7. The regional summary runs as a single-threaded LINQ query after every row is already in memory.

## 3. Challenge: Fix the Nightly Import

Recommended time: 25 minutes.

> **Note:** Please avoid letting AI Agents solve the challenges for you. You're smart. You got this. Use them to understand the existing code, clarify parallel programming concepts, interpret errors, and ask questions that help you decide what to change. The goal is to practice the reasoning yourself.

Refactor **Services/ImportService.cs** so all three stages use the data parallelism primitive that fits them. Do not change **CustomerApiService.cs**, **OrderFileService.cs**, **OrderRow.cs**, or the page.

Requirements:

1. Set `MaxDegreeOfParallelism` and `CancellationToken` through `ParallelOptions`, using the token that `RunImportAsync` already receives. The CPU stage and the I/O stage do not have to agree on the same ceiling.
2. Replace the sequential validation `foreach` so every processor scores rows at the same time.
3. Leave `ScoreRisk(OrderRow)` exactly as it is. The goal is to run it on more threads, not to make it cheaper.
4. Fix the enrichment stage so all 4,000 rows really are enriched, and so `RunImportAsync` does not return until they are.
5. Use the `Parallel` overload that is built for asynchronous bodies, and forward the `CancellationToken` that overload hands to your body.
6. Bound the enrichment stage so you do not fire 4,000 calls at the customer API at once. Pick a limit and be ready to explain the number you picked.
7. Turn the regional summary into a PLINQ query that honors the cancellation token.
8. Do not add a shared counter, list, or dictionary that parallel bodies write to. If you find yourself wanting one, stop and work out why you do not need it here.

Acceptance checks:

1. **ImportPortal.slnx** builds.
2. At [http://localhost:5011](http://localhost:5011), **Validated** still reads 4,000 of 4,000, and its time is a fraction of the validation time you wrote down before you changed anything.
3. **Enriched** reads 4,000 of 4,000, and its card is green instead of red.
4. The **Enriched** time is now measured in seconds rather than hundredths of a second, because the app is finally making 4,000 real API calls. Divide 4,000 by the limit you chose and multiply by the 10 millisecond call time to predict roughly what the card should say, then check it against your prediction.
5. On a machine with several processors, the **Total** card is at or below the total you recorded from the starter, even though the app is now doing thousands of calls it used to skip.
6. **Regional summary** still shows US-CA, US-NY, US-TX, and US-WA with 1,000 orders each, and the revenue for each region is unchanged.
7. Clicking **Run import** several times in a row gives the same counts every time, and no card turns red.
8. Your code is ready to compare with **2. Finish/ImportPortal**.

## 4. Review the Solution

After you have attempted the challenge, pause here for group review.

We will compare approaches, talk about which stage deserved which primitive and what limit you chose for the customer API, and answer questions before opening [SOLUTION.md](SOLUTION.md) together.

The solution walkthrough rebuilds the import one stage at a time and points you to the completed code in **2. Finish/ImportPortal**.
