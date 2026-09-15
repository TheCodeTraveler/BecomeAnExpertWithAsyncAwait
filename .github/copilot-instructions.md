# Guidelines for AI Agents Working in This Repository

This repository is the attendee-facing material for Brandon Minnick's two-day workshop **"Become An Expert With Async Await in C#"**. Attendees clone it, read the READMEs, and run the samples on their own laptops. Optimize every change for the attendee experience and for the teaching goal, not for a production codebase.

Most attendees open the starter project and jump straight into the code without reading the rest of the README. Every challenge app therefore has to guide an attendee through the whole challenge by itself, through its workshop guide and the comments in the code, without sending them back to the README for instructions, clues, or hints.

## What lives where

- Numbered top-level folders are workshop sections and must stay in teaching order. Day 1 is asynchronous programming: `0. Prerequisites`, `1. Thread Switching and Compiler Generated Code` (slides only), `2. Correcting Common Async Await Mistakes`, `3. .NET Internals`, `4. Creating Custom Implementation of Task`. Day 2 is parallel programming: `5. Asynchronous vs Parallel Programming`, `6. Coordinating Multiple Tasks`, `7. Data Parallelism`, `8. Concurrent Collections`, `9. Channels`.
- Day 2 material was adapted from Brandon's Dometrain course "From Zero to Hero: Parallel Programming in C#". Dometrain attendees said that course's console samples were not realistic enough, so every Day 2 sample is a Blazor Server app built around a bug a working .NET developer would actually ship: `OrderPortal` (singleton services under concurrent load), `ProductDetails` (a page awaiting its backend services one at a time), `ImportPortal` (a batch job that is sequential and uses `Parallel.ForEach` with an `async` lambda), `StockWatch` (a live dashboard with unsynchronized collections), and `TelemetryPipeline` (a webhook handler doing slow work inline). Keep new samples in that register: a realistic app with a realistic mistake.
- Each hands-on section has a `README.md` (attendee-facing challenge) and a `SOLUTION.md` (step-by-step walkthrough the host presents after the challenge and group review). Sections 2 and 4 through 9 have `1. Start` (intentionally imperfect) and `2. Finish` (completed) projects. Section 4's `CoffeeShop` Blazor app differs between Start and Finish only in `CustomTask.cs` (stubs that throw `NotImplementedException` with hints) and its port; its `Steps` folder is the attendee's guide, and the app re-verifies Steps 1 to 5 against the attendee's `CustomTask` on startup.
- Every challenge app uses the guided steps format that Section 4 introduced. In Sections 2 and 5 through 9 the realistic page (Top stories, Checkout, Product, Import, Dashboard, Ingest) is the app itself at `/`, and `Components/Layout/WorkshopGuide.razor` is docked beside it on every screen: a Your challenge tab, one tab per step, progress, and the step's results and hints. Attendees missed a separate steps page behind a top-bar link, so never move the guide back behind navigation. Each of those apps has its own visual identity and an in-world guide (HackerNews a pull request code review, OrderPortal a launch checklist), while the guide's structure and labels stay the same everywhere. Sections 3 and 4 have no separate realistic page, so their apps open on a steps overview (InternalsLab's Workshop steps page, CoffeeShop's Home page) with a page per step; CoffeeShop's checks show expected results without per-check hints, and InternalsLab's steps are investigations. In Sections 2 and 5 through 9, each `Steps/Step*.cs` file is one bug or concept with a story, how to see it in the app, where to look, a task list, clues, and checks that run the attendee's real code and attach a hint to every expected result. In Sections 2 and 5 through 9, `Verification/StepVerifier.cs` checks every step on startup, in order, stops at the first failure, and logs each failed hint to the app's log output; CoffeeShop does the same for Steps 1 to 5, and InternalsLab records progress in its `LabNotebook` instead. A step unlocks only after the step before it passes. In Sections 2 and 5 through 9, the `Steps` and `Verification` folders, the layout with its workshop guide, the realistic page's markup, and `app.css` are identical in Start and Finish.
- `3. .NET Internals` is one guided Blazor app, `InternalsLab`, with no Start or Finish. Its four steps (ThreadStatic, ExecutionContext, Principal, SynchronizationContext) are investigations: the attendee records predictions, runs the experiment, compares results, and answers multiple-choice explain questions, and a step passes once its experiment has run and every question is answered correctly. State lives in a singleton `LabNotebook` for as long as the app runs, and the `Steps` files hold the answers. The Principal experiment is an MVC controller with cookie sign-in inside the same app, because it has to observe a real ASP.NET Core request. Attendees have never seen this code base, so step text never says "read the code" without saying exactly where: every checkpoint, Try it suggestion, and hint that points at code links to the exact line, on GitHub or in VS Code as the attendee chooses. Step text writes those links as `[File.cs](path#text on the line)`, and `Steps/SourceCode.cs` finds the line when the page renders, so a link follows edits. Checkpoint links find the `// Step N: checkpoint M.` comment above each recording call, so keep that comment format.
- Section numbers are embedded in paths in the root `README.md`, `0. Prerequisites/README.md`, and the module READMEs (`build.yml` and `dependabot.yml` build and scan from the repository root, so they name no section). When renaming or reordering sections, update all of them and grep for stale numbers before finishing.
- Every sample is a Blazor Server, ASP.NET Core MVC, or console app on .NET 10. No MAUI, no mobile workloads, no emulators: attendees only have the .NET SDK, an editor, and a browser.
- Samples must run offline. `8. Concurrent Collections` uses a simulated in-process market feed (`MarketDataService`) rather than a real quote API, so there is no API key to leak and no rate limit to hit in a room full of attendees. The HackerNews Top stories page is the one exception, and its workshop steps still run offline against a fake in-memory `IHackerNewsAPI`.

## Non-negotiable rules

1. **Code and walkthroughs stay in sync, in both directions.** Any change to sample code must be reflected in that section's `README.md` and `SOLUTION.md` (snippets and prose), and any walkthrough change must be reflected in the code. The app's step text (story, tasks, clues, and check hints) is part of the walkthrough too. Check every duplicated copy: Start and Finish.
2. **Never "fix" the Start projects.** Code marked `// ToDo Refactor (Step N)` and the compiler warnings it produces (for example CS4014) *are* the challenge. The step number names the workshop step that checks that line. Do not refactor it, suppress its warnings, or run analyzer-based formatting over it. The only permitted change is the element ordering described under "C# element ordering" below: moving a member, with its `// ToDo Refactor` comment attached, never changes what it does. Every step must fail on Start for its own reason and pass on Finish, reliably: amplify race conditions in the checks rather than hoping a single run catches them.
3. **Write for attendees, not the host.** READMEs and the in-app step text must flow naturally into the challenge without revealing the answer. Tasks and hints stay at the level of the README requirements; clues can be more direct because attendees open them on purpose. Solutions belong only in `SOLUTION.md` and the `2. Finish` folders.
4. **Discourage AI from solving the challenges.** Every challenge begins with a note whose first sentences are exactly: `> **Note:** Please avoid letting AI Agents solve the challenges for you. You're smart. You got this.` followed by what AI *may* be used for (understanding the code, clarifying concepts, interpreting errors). Every challenge app repeats it in its Your challenge panel.
5. **Keep it simple.** Make the smallest correct change. No null-forgiving operator (`!`), no `#pragma` suppressions, no abstractions for one-off work, and no committed helper tooling or scripts beyond `BuildAllSolutions.ps1`. If a bulk edit is needed, do the edit rather than adding automation to the repo.
6. **New style rules are warnings, not errors,** and live in `.editorconfig` (a few naming and correctness rules there are errors; do not add more). That includes the StyleCop element-ordering rules, which `Directory.Build.props` suppresses with `NoWarn` in every configuration except Release so attendees working in Debug never see them. Do not add build-blocking style enforcement that would interrupt attendees while they iterate; ordering is enforced by the Release builds in `BuildAllSolutions.ps1`, which attendees do not run.
7. **The Prerequisites README must stay foolproof.** It has to work on Windows and macOS, with or without admin rights, with Visual Studio, Rider, or VS Code, with Git blocked (ZIP download), and behind corporate networks that block Hacker News. Do not remove a path just because it looks redundant.

## Build and validate

- Full validation, required before finishing any code change: `pwsh ./BuildAllSolutions.ps1`. It runs `dotnet format whitespace` and then `dotnet build -c Release --no-incremental` for every `.slnx`, and it fails when any build reports a StyleCop element-ordering warning (`SA1201`, `SA1202`, `SA1204`, `SA1214`, `SA1215`). Keep the script on `dotnet format whitespace`: full `dotnet format` fails on the .NET 10 SDK and analyzer/code-style formatting would erase the intentional Start warnings.
- Single solution: `dotnet build "<section>/<folder>/<Name>.slnx"`.
- Run a web sample: `dotnet run --project "<path>/<Name>.csproj"`. Ports are fixed and referenced by the READMEs: HackerNews Start `5001` / Finish `5002`, InternalsLab `5003`, StockWatch Start `5005` / Finish `5006`, OrderPortal Start `5007` / Finish `5008`, ProductDetails Start `5009` / Finish `5010`, ImportPortal Start `5011` / Finish `5012`, TelemetryPipeline Start `5013` / Finish `5014`, CoffeeShop Start `5015` / Finish `5016`.
- `Properties/launchSettings.json` is gitignored, but every web sample's copy is force-tracked because `ASPNETCORE_ENVIRONMENT=Development` is required for `dotnet run` to serve `_framework/blazor.web.js`. Keep them tracked and keep the environment variable.
- A step's checks create their own instances of the services they exercise, never the app's singletons, and must never crash the app: an unhandled exception on a thread pool thread, a timer callback, or an `async void` ends the process, so gate any check that could provoke one behind the step that fixes it. Bugs inside a page's `*.razor.cs` are checked by rendering a verifier-only subclass of the page with `HtmlRenderer`.

## Code conventions beyond .editorconfig

- .NET 10, `LangVersion preview`, nullable enabled, and `WarningsAsErrors` includes nullable warnings; see `Directory.Build.props`. Package versions are managed centrally in `Directory.Packages.props`.
- Blazor pages inherit a `*PageBase` partial class defined in `*.razor.cs`. Injected services use `[Inject] public required T Name { get; init; }` rather than nullable fields or `!`.
- After `ConfigureAwait(false)`, marshal component state changes through `InvokeAsync(...)`. In realistic app code, log full exceptions server-side and show users a generic, actionable message; never send exception text to the browser. Workshop plumbing is the exception: the workshop guides, CoffeeShop's and InternalsLab's pages, and StockWatch's Feed fault panel show the full exception on the page. Attendees run the apps from an IDE as often as from a terminal, so never send them to "the terminal running the app"; when the app process itself exits, point them at their IDE's Run or Debug output, or the terminal if they used `dotnet run`.
- Forward `CancellationToken`s to every cancellable API and catch `OperationCanceledException` only `when (token.IsCancellationRequested)`.
- `ExecutionContext.SuppressFlow()` returns a thread-affine `AsyncFlowControl`: create the task inside the `using` block and `await` it after the block, never inside it.
- Finished samples are what attendees compare their work against. They must demonstrate the practice being taught even when a shortcut would compile, and they should build without warnings.

## C# element ordering

Every `.cs` file, including the `1. Start` projects and `*.razor.cs` code-behind files, follows the StyleCop ordering rules [SA1201](https://github.com/DotNetAnalyzers/StyleCopAnalyzers/blob/master/documentation/SA1201.md), [SA1202](https://github.com/DotNetAnalyzers/StyleCopAnalyzers/blob/master/documentation/SA1202.md), [SA1204](https://github.com/DotNetAnalyzers/StyleCopAnalyzers/blob/master/documentation/SA1204.md), [SA1214](https://github.com/DotNetAnalyzers/StyleCopAnalyzers/blob/master/documentation/SA1214.md), and [SA1215](https://github.com/DotNetAnalyzers/StyleCopAnalyzers/blob/master/documentation/SA1215.md). `StyleCop.Analyzers` is referenced by every project through `Directory.Packages.props`, `.editorconfig` enables only these five rules (as warnings), `Directory.Build.props` adds them to `NoWarn` outside Release builds, and `BuildAllSolutions.ps1` builds in Release and fails when any of them fire. A Debug build (`dotnet build`, `dotnet run`, or an IDE) never reports them, so check ordering with a Release build. A code change is not finished until every build is free of `SA` warnings.

Elements at the file root level or within a namespace are positioned in this order:

1. Extern alias directives
2. Using directives
3. Namespaces
4. Delegates
5. Enums
6. Interfaces
7. Records
8. Structs
9. Classes

Within a class, struct, or interface, elements are positioned in this order:

1. Fields
2. Constructors
3. Finalizers (destructors)
4. Delegates
5. Events
6. Enums
7. Interfaces
8. Properties
9. Indexers
10. Methods
11. Records
12. Structs
13. Classes

Within each of those groups:

- Order by access: `public`, `internal`, `protected internal`, `protected`, `private protected`, `private`. A member with no access modifier is `private`; keep that style rather than adding `private` to sort it.
- `static` members come before instance members of the same access level (SA1204).
- `const` fields come first, then `readonly` fields, then the rest (SA1214, SA1215).
- Members that tie on everything above keep their existing relative order.

Move a member as a unit: its attributes, comments, `// ToDo Refactor` markers, and the blank line separating it from its neighbour travel with it, and the member itself is never edited while being moved. `dotnet format analyzers` cannot apply StyleCop fixes on the .NET 10 SDK, so reorder by hand and rebuild until no `SA` warnings remain.

## Markdown conventions

- Numbered lists use incrementing numbers (`1.`, `2.`, `3.`), never repeated `1.` markers.
- Each challenge states a recommended time and ends by pausing for group review before `SOLUTION.md`. The times are budgeted against the two-day schedule in the root `README.md`. Each day's table totals 345 scheduled minutes, and Day 1's total already includes the 60-minute setup block. The venue schedules its own lunch and snack breaks around these tables, and timings flex to the room. Coding challenges run 20 to 45 minutes; the .NET Internals investigations run 8 to 12 minutes each. If you change a challenge time, update the schedule table so both still add up.
- Code snippets in `SOLUTION.md` must match the `2. Finish` source exactly.
- Links into numbered folders need `%20`-encoded spaces. Verify that every changed link resolves.

## Pull requests and code review

- Review comments that question continuation semantics, `ExecutionContext` flow, or anything shown to the browser have historically been correct and led to real fixes. Take them seriously, but confirm each against the teaching goal before changing walkthrough text.
- Do not flag the intentional mistakes in `1. Start` projects as defects.
- When a review comment leads to a code change, apply the same change to every duplicated copy and to the matching walkthrough in the same PR.
