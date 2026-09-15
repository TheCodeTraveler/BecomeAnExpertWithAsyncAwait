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

Coding challenges run 20 to 45 minutes depending on how much code they ask you to write. The shorter investigation challenges in the .NET Internals section focus on runtime observations and discussion rather than large code changes.

Every challenge app walks you through its challenge one step at a time. Run the app and its **workshop guide** tells you which step to work on next, and unlocks each step when the one before it passes. Each step explains what to do and where to look, and shows what actually happened next to what was expected. Most steps also give you a hint for anything that does not match, with clues if you get stuck.

> **Note:** Please avoid letting AI Agents solve the challenges for you. You're smart. You got this. Use AI Agents to understand the existing code, clarify concepts, interpret errors, and ask questions that help you decide what to do next. The goal is to practice the reasoning yourself.

## Schedule

Both days run from 09:00 to 17:00. Breakfast is served from 08:00, there is a short break mid-morning and mid-afternoon, and lunch is served at the restaurant on the second floor.

Day 1 opens with nearly an hour of setup. Everyone installs the .NET 10 SDK, picks an editor, clones the repo, and builds a sample before we teach anything. Nobody should spend the first coding challenge fighting an install. Work through [0. Prerequisites](https://github.com/TheCodeTraveler/BecomeAnExpertWithAsyncAwait/tree/main/0.%20Prerequisites#0-install-prerequisites) before you arrive if you can, and we will use that time to fix whatever did not work. If you want a head start, arrive during breakfast and begin the install then.

Day 1 then covers asynchronous programming: what the compiler does to your async methods, the mistakes everyone makes, the internal machinery that makes it all work, and building a `Task` from scratch.

Day 2 covers parallel programming: using many threads on purpose, and keeping shared state correct while you do.

### Day 1: Setup and Asynchronous Programming

| Time | Length | Topic |
| --- | --- | --- |
| 09:00 - 09:30 | 30 min | Setup and installation: .NET 10 SDK, IDE, Clone Repo |
| 09:30 - 10:15 | 45 min | 1. Thread Switching and Compiler-Generated Code (Lecture) |
| 10:15 - 10:30 | 15 min | Morning break |
| 10:30 - 11:05 | 35 min | 2. Correcting Common Async/Await Mistakes (Lecture) |
| 11:05 - 11:40 | 35 min | 2. Correcting Common Async/Await Mistakes (Coding Challenge) |
| 11:40 - 12:00 | 20 min | 2. Correcting Common Async/Await Mistakes (Group Review) |
| 12:00 - 13:00 | 60 min | Lunch |
| 13:00 - 13:30 | 30 min | 3. .NET Internals (Lecture) |
| 13:30 - 13:50 | 20 min | 3. .NET Internals (Coding "Challenge") |
| 13:50 - 14:10 | 20 min | 3. .NET Internals (Group Review) |
| 14:10 - 14:30 | 20 min | 4. Creating a Custom `Task` (Lecture) |
| 14:30 - 15:15 | 45 min | 4. Creating a Custom `Task` (Coding Challenge) |
| 15:15 - 15:30 | 15 min | Afternoon break |
| 15:30 - 16:00 | 15 min | 4. Creating a Custom `Task` (Challenge, continued) |
| 16:00 - 17:00 | 25 min | 4. Creating a Custom `Task` (Group Review) |

### Day 2: Parallel Programming

| Time | Length | Topic |
| --- | --- | --- |
| 09:00 - 09:15 | 15 min | Day 1 Recap |
| 09:15 - 09:40 | 25 min | 5. Asynchronous vs Parallel Programming (Lecture) |
| 09:40 - 10:05 | 25 min | 5. Asynchronous vs Parallel Programming (Coding Challenge) |
| 10:05 - 10:15 | 10 min | 5. Asynchronous vs Parallel Programming (Group Review) |
| 10:15 - 10:30 | 15 min | Morning break |
| 10:30 - 10:50 | 20 min | 5. Asynchronous vs Parallel Programming (Group Review, continued) |
| 10:50 - 11:15 | 25 min | 6. Coordinating Multiple Tasks (Lecture) |
| 11:15 - 11:35 | 20 min | 6. Coordinating Multiple Tasks (Coding Challenge) |
| 11:35 - 12:00 | 25 min | 6. Coordinating Multiple Tasks (Group Review) |
| 12:00 - 13:00 | 60 min | Lunch |
| 13:00 - 13:20 | 20 min | 7. Data Parallelism (Lecture) |
| 13:20 - 13:45 | 25 min | 7. Data Parallelism (Coding Challenge) |
| 13:45 - 14:10 | 25 min | 7. Data Parallelism (Group Review) |
| 14:10 - 14:30 | 20 min | 8. Concurrent Collections (Lecture) |
| 14:30 - 14:55 | 25 min | 8. Concurrent Collections (Coding Challenge) |
| 14:55 - 15:15 | 20 min | 8. Concurrent Collections (Group Review) |
| 15:15 - 15:30 | 15 min | Afternoon break |
| 15:30 - 15:45 | 15 min | 9. Channels (Lecture) |
| 15:45 - 16:05 | 20 min | 9. Channels (Coding Challenge) |
| 16:05 - 16:25 | 25 min | 9. Channels (Group Review) |
| 16:25 - 16:45 | 20 min | Final Review, Recap and Q&A |
| 16:45 - 17:00 | 10 min | Workshop evaluation |

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

## Additional Resources

Join me in these [DomeTrain](https://dometrain.com) courses where we'll learn everything you need to know to master asynchronous programming using async await in C# and .NET.

[![Asynchronous Programming](https://github.com/user-attachments/assets/e3ce2f9b-7fb5-4103-9b00-46d1aea5c977)](https://dometrain.com/course/from-zero-to-hero-asynchronous-programming-in-csharp/)

[![Parallel Programming](https://github.com/user-attachments/assets/8567ec85-83d2-493b-bd9e-0a6dd42af60d)](https://dometrain.com/course/from-zero-to-hero-parallel-programming-in-csharp/)

Blazor documentation: [https://learn.microsoft.com/aspnet/core/blazor/](https://learn.microsoft.com/aspnet/core/blazor/)
