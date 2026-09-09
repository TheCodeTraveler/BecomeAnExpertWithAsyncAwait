# Become An Expert With Async Await in C\#

[![Build](https://github.com/TheCodeTraveler/BecomeAnExpertWithAsyncAwait/actions/workflows/build.yml/badge.svg)](https://github.com/TheCodeTraveler/BecomeAnExpertWithAsyncAwait/actions/workflows/build.yml)

In this workshop, we will dive deep into how .NET implements asynchronous programming under the hood to become experts using async/await!

Join me as we uncover the ways that the .NET compiler modifies each of our async methods. We'll dive deep into the .NET source code to understand the importance of internal framework tools like `SynchronizationContext`, `ExecutionContext`, `Principal`, `ThreadStatic`, and more. Then we'll use that knowledge to build a custom implementation of `Task` from scratch and use it with the built-in async/await keywords.

On day two we move from asynchronous to parallel. We'll learn the difference between waiting and working, hunt down race conditions and deadlocks, coordinate groups of tasks with `Task.WhenAll`, `Task.WhenAny`, and `Task.WhenEach`, put every core to work with the `Parallel` class and PLINQ, keep shared state safe with the concurrent collections, and connect producers to consumers with `System.Threading.Channels`.

The interactive UI samples use Blazor so attendees can run them in a browser with only the .NET SDK installed.

![QR code](https://github.com/user-attachments/assets/6c94fc5c-c71d-4471-9cfb-5026824a6ec5)

## Workshop Format

This workshop mixes short lectures, timed implementation challenges, group review, and guided solution walkthroughs.

Each hands-on section follows the same rhythm:

1. We introduce the topic together and connect it to the code you are about to change.
2. You open the starter project and inspect the code that matters for the lesson.
3. You attempt a timed challenge to implement the ideas that were just taught.
4. We review attendee approaches together, including tradeoffs, questions, and common mistakes.
5. We walk through the solution step by step as a group, reiterating the key concepts along the way.
6. You compare your implementation with the completed sample and ask any remaining questions while the context is fresh.

Coding challenges run 20 to 45 minutes depending on how much code they ask you to write. The shorter investigation challenges in the .NET Internals section focus on debugger observations and discussion rather than large code changes.

> **Note:** Please avoid letting AI Agents solve the challenges for you. You're smart. You got this. Use AI Agents to understand the existing code, clarify concepts, interpret errors, and ask questions that help you decide what to do next. The goal is to practice the reasoning yourself.

## Schedule

Day 1 opens with a full hour of setup. Everyone installs the .NET 10 SDK, picks an editor, clones the repo, and builds a sample before we teach anything. Nobody should spend the first coding challenge fighting an install. Work through [0. Prerequisites](https://github.com/TheCodeTraveler/BecomeAnExpertWithAsyncAwait/tree/main/0.%20Prerequisites#0-install-prerequisites) before you arrive if you can, and we will use the hour to fix whatever did not work.

Day 1 then covers asynchronous programming: what the compiler does to your async methods, the mistakes everyone makes, the internal machinery that makes it all work, and building a `Task` from scratch.

Day 2 covers parallel programming: using many threads on purpose, and keeping shared state correct while you do.

### Day 1: Setup and Asynchronous Programming

| Time | Topic |
| --- | --- |
| 60 min | Setup and installation: .NET 10 SDK, editor, clone, first build |
| 20 min | Thread switching and compiler-generated code lecture |
| 20 min | Correcting common async/await mistakes lecture |
| 35 min | Challenge: refactor the HackerNews refresh flow |
| 10 min | Group review of attendee approaches |
| 15 min | Step-by-step solution walkthrough |
| 20 min | .NET Internals lecture |
| 40 min | Four investigation challenges: `ThreadStatic`, `Principal`, `ExecutionContext`, `SynchronizationContext` |
| 20 min | Group review and solution walkthrough |
| 25 min | Custom `Task` implementation lecture |
| 45 min | Challenge: build an awaitable `CustomTask` |
| 10 min | Group review of attendee approaches |
| 25 min | Step-by-step solution walkthrough and Q&A |

### Day 2: Parallel Programming

| Time | Topic |
| --- | --- |
| 10 min | Day 1 recap and Day 2 orientation |
| 25 min | Asynchronous vs parallel programming lecture |
| 25 min | Challenge: fix OrderPortal's races and deadlock |
| 25 min | Group review and solution walkthrough |
| 15 min | Coordinating multiple tasks lecture |
| 20 min | Challenge: fan out the ProductDetails page |
| 20 min | Group review and solution walkthrough |
| 20 min | Data parallelism lecture: the `Parallel` class and PLINQ |
| 25 min | Challenge: speed up the ImportPortal batch job |
| 25 min | Group review and solution walkthrough |
| 20 min | Concurrent collections lecture |
| 25 min | Challenge: make the StockWatch dashboard thread safe |
| 25 min | Group review and solution walkthrough |
| 15 min | Channels lecture |
| 20 min | Challenge: put a channel behind the TelemetryPipeline webhook |
| 20 min | Group review and solution walkthrough |
| 10 min | Final review, recap and Q&A |

## Agenda

0. [Install Prerequisites](https://github.com/TheCodeTraveler/BecomeAnExpertWithAsyncAwait/tree/main/0.%20Prerequisites#0-install-prerequisites)
1. [(Presentation) Thread Switching + Compiler Generated Code](https://github.com/TheCodeTraveler/BecomeAnExpertWithAsyncAwait/blob/main/1.%20Thread%20Switching%20and%20Compiler%20Generated%20Code/ThreadSwitchingAndCompilerGeneratedCode.pptx)
2. [(Presentation/Code) Correct Common Async Await Mistakes](https://github.com/TheCodeTraveler/BecomeAnExpertWithAsyncAwait/tree/main/2.%20Correcting%20Common%20Async%20Await%20Mistakes)
3. [(Presentation/Code) .NET Internals](https://github.com/TheCodeTraveler/BecomeAnExpertWithAsyncAwait/tree/main/3.%20.NET%20Internals)
4. [(Presentation/Code) Creating Custom Implementation of Task](https://github.com/TheCodeTraveler/BecomeAnExpertWithAsyncAwait/tree/main/4.%20Creating%20Custom%20Implementation%20of%20Task)
5. [(Presentation/Code) Asynchronous vs Parallel Programming](https://github.com/TheCodeTraveler/BecomeAnExpertWithAsyncAwait/tree/main/5.%20Asynchronous%20vs%20Parallel%20Programming)
6. [(Presentation/Code) Coordinating Multiple Tasks](https://github.com/TheCodeTraveler/BecomeAnExpertWithAsyncAwait/tree/main/6.%20Coordinating%20Multiple%20Tasks)
7. [(Presentation/Code) Data Parallelism](https://github.com/TheCodeTraveler/BecomeAnExpertWithAsyncAwait/tree/main/7.%20Data%20Parallelism)
8. [(Presentation/Code) Concurrent Collections](https://github.com/TheCodeTraveler/BecomeAnExpertWithAsyncAwait/tree/main/8.%20Concurrent%20Collections)
9. [(Presentation/Code) Channels](https://github.com/TheCodeTraveler/BecomeAnExpertWithAsyncAwait/tree/main/9.%20Channels)
10. [(Presentation) Recap, Resources and Thank You](https://github.com/TheCodeTraveler/BecomeAnExpertWithAsyncAwait/blob/main/10.%20Thank%20You/ThankYou.pptx)

## Additional Resources

Join me in these [DomeTrain](https://dometrain.com) courses where we'll learn everything you need to know to master asynchronous programming using async await in C# and .NET.

[![Asynchronous Programming](https://github.com/user-attachments/assets/e3ce2f9b-7fb5-4103-9b00-46d1aea5c977)](https://dometrain.com/course/from-zero-to-hero-asynchronous-programming-in-csharp/)

[![Parallel Programming](https://github.com/user-attachments/assets/8567ec85-83d2-493b-bd9e-0a6dd42af60d)](https://dometrain.com/course/from-zero-to-hero-parallel-programming-in-csharp/)

Blazor documentation: [https://learn.microsoft.com/aspnet/core/blazor/](https://learn.microsoft.com/aspnet/core/blazor/)
