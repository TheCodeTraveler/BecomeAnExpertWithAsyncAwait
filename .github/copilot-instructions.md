# Guidelines for AI Agents Working in This Repository

This repository is the attendee-facing material for Brandon Minnick's two-day workshop **"Become An Expert With Async Await in C#"**. Attendees clone it, read the READMEs, and run the samples on their own laptops. Optimize every change for the attendee experience and for the teaching goal, not for a production codebase.

## What lives where

- Numbered top-level folders are workshop sections and must stay in teaching order. Day 1 is asynchronous programming: `0. Prerequisites`, `1. Thread Switching and Compiler Generated Code` (slides only), `2. Correcting Common Async Await Mistakes`, `3. .NET Internals`, `4. Creating Custom Implementation of Task`. Day 2 is parallel programming: `5. Asynchronous vs Parallel Programming`, `6. Coordinating Multiple Tasks`, `7. Data Parallelism`, `8. Concurrent Collections`, `9. Channels`.
- Day 2 material was adapted from Brandon's Dometrain course "From Zero to Hero: Parallel Programming in C#". Dometrain attendees said that course's console samples were not realistic enough, so every Day 2 sample is a Blazor Server app built around a bug a working .NET developer would actually ship: `OrderPortal` (singleton services under concurrent load), `ProductDetails` (a page awaiting its backend services one at a time), `ImportPortal` (a batch job that is sequential and uses `Parallel.ForEach` with an `async` lambda), `StockWatch` (a live dashboard with unsynchronized collections), and `TelemetryPipeline` (a webhook handler doing slow work inline). Keep new samples in that register: a realistic app with a realistic mistake.
- Each hands-on section has a `README.md` (attendee-facing challenge) and a `SOLUTION.md` (step-by-step walkthrough the host presents after the challenge and group review). Sections 2 and 4 through 9 have `1. Start` (intentionally imperfect) and `2. Finish` (completed) projects.
- `3. .NET Internals/4. SynchronizationContext/HackerNews` is a copy of `2. Correcting Common Async Await Mistakes/2. Finish/HackerNews` plus debugger-inspection locals in `News.razor.cs`. Changes to one usually belong in the other.
- Section numbers are embedded in paths in the root `README.md`, `.github/workflows/build.yml`, `.github/dependabot.yml`, and the module READMEs. When renaming or reordering sections, update all of them and grep for stale numbers before finishing.
- Every sample is a Blazor Server, ASP.NET Core MVC, or console app on .NET 10. No MAUI, no mobile workloads, no emulators: attendees only have the .NET SDK, an editor, and a browser.
- Samples must run offline. `8. Concurrent Collections` uses a simulated in-process market feed (`MarketDataService`) rather than a real quote API, so there is no API key to leak and no rate limit to hit in a room full of attendees.

## Non-negotiable rules

1. **Code and walkthroughs stay in sync, in both directions.** Any change to sample code must be reflected in that section's `README.md` and `SOLUTION.md` (snippets and prose), and any walkthrough change must be reflected in the code. Check every duplicated copy: Start, Finish, and the SynchronizationContext copy.
2. **Never "fix" the Start projects.** Code marked `// ToDo Refactor` and the compiler warnings it produces (for example CS4014) *are* the challenge. Do not refactor it, suppress its warnings, or run analyzer-based formatting over it.
3. **Write for attendees, not the host.** READMEs must flow naturally into the challenge without revealing the answer. Solutions belong only in `SOLUTION.md` and the `2. Finish` folders.
4. **Discourage AI from solving the challenges.** Every challenge begins with a note whose first sentences are exactly: `> **Note:** Please avoid letting AI Agents solve the challenges for you. You're smart. You got this.` followed by what AI *may* be used for (understanding the code, clarifying concepts, interpreting errors).
5. **Keep it simple.** Make the smallest correct change. No null-forgiving operator (`!`), no `#pragma` suppressions, no abstractions for one-off work, and no committed helper tooling or scripts beyond `BuildAllSolutions.ps1`. If a bulk edit is needed, do the edit rather than adding automation to the repo.
6. **Style rules are warnings, not errors,** and live in `.editorconfig`. Do not add build-blocking style enforcement that would interrupt attendees while they iterate.
7. **The Prerequisites README must stay foolproof.** It has to work on Windows and macOS, with or without admin rights, with Visual Studio, Rider, or VS Code, with Git blocked (ZIP download), and behind corporate networks that block Hacker News. Do not remove a path just because it looks redundant.

## Build and validate

- Full validation, required before finishing any code change: `pwsh ./BuildAllSolutions.ps1`. It runs `dotnet format whitespace` and then `dotnet build -c Release` for every `.slnx`. Keep the script on `dotnet format whitespace`: full `dotnet format` fails on the .NET 10 SDK and analyzer/code-style formatting would erase the intentional Start warnings.
- Single solution: `dotnet build "<section>/<folder>/<Name>.slnx"`.
- Run a web sample: `dotnet run --project "<path>/<Name>.csproj"`. Ports are fixed and referenced by the READMEs: PrincipalExample `5000`, HackerNews Start `5001` / Finish `5002`, SynchronizationContext HackerNews `5004`, StockWatch Start `5005` / Finish `5006`, OrderPortal Start `5007` / Finish `5008`, ProductDetails Start `5009` / Finish `5010`, ImportPortal Start `5011` / Finish `5012`, TelemetryPipeline Start `5013` / Finish `5014`.
- `Properties/launchSettings.json` is gitignored, but the HackerNews copies are force-tracked because `ASPNETCORE_ENVIRONMENT=Development` is required for `dotnet run` to serve `_framework/blazor.web.js`. Keep them tracked and keep the environment variable.

## Code conventions beyond .editorconfig

- .NET 10, `LangVersion preview`, nullable enabled, and `WarningsAsErrors` includes nullable warnings; see `Directory.Build.props`. Package versions are managed centrally in `Directory.Packages.props`.
- Blazor pages inherit a `*PageBase` partial class defined in `*.razor.cs`. Injected services use `[Inject] public required T Name { get; init; }` rather than nullable fields or `!`.
- After `ConfigureAwait(false)`, marshal component state changes through `InvokeAsync(...)`. Log full exceptions server-side and show users a generic, actionable message; never send exception text to the browser.
- Forward `CancellationToken`s to every cancellable API and catch `OperationCanceledException` only `when (token.IsCancellationRequested)`.
- `ExecutionContext.SuppressFlow()` returns a thread-affine `AsyncFlowControl`: create the task inside the `using` block and `await` it after the block, never inside it.
- Finished samples are what attendees compare their work against. They must demonstrate the practice being taught even when a shortcut would compile, and they should build without warnings.

## Markdown conventions

- Numbered lists use incrementing numbers (`1.`, `2.`, `3.`), never repeated `1.` markers.
- Each challenge states a recommended time and ends by pausing for group review before `SOLUTION.md`. The times are budgeted against the two-day schedule in the root `README.md`. Each day's table totals 345 scheduled minutes, and Day 1's total already includes the 60-minute setup block. The venue schedules its own lunch and snack breaks around these tables, and timings flex to the room. Coding challenges run 20 to 45 minutes; the .NET Internals investigations run 8 to 12 minutes each. If you change a challenge time, update the schedule table so both still add up.
- Code snippets in `SOLUTION.md` must match the `2. Finish` source exactly.
- Links into numbered folders need `%20`-encoded spaces. Verify that every changed link resolves.

## Pull requests and code review

- Review comments that question continuation semantics, `ExecutionContext` flow, or anything shown to the browser have historically been correct and led to real fixes. Take them seriously, but confirm each against the teaching goal before changing walkthrough text.
- Do not flag the intentional mistakes in `1. Start` projects as defects.
- When a review comment leads to a code change, apply the same change to every duplicated copy and to the matching walkthrough in the same PR.
