# Become An Expert With Async Await in C\#

[![Build](https://github.com/TheCodeTraveler/BecomeAnExpertWithAsyncAwait/actions/workflows/build.yml/badge.svg)](https://github.com/TheCodeTraveler/BecomeAnExpertWithAsyncAwait/actions/workflows/build.yml)

In this workshop, we will dive deep into how .NET implements asynchronous programming under the hood to become experts using async/await!

Join me as we uncover the ways that the .NET compiler modifies each of our async methods. We'll learn how to build a custom implementation of `Task` from scratch and use it with the built-in async/await keywords. Then we'll dive deep into the .NET source code to understand the importance of internal framework tools like SynchronizationContext, ExecutionContext, Principal, ThreadStatic and more!

![QR code](https://github.com/user-attachments/assets/6c94fc5c-c71d-4471-9cfb-5026824a6ec5)

## Workshop Format

This workshop mixes short lectures, timed implementation challenges, group review, and guided solution walkthroughs.

Each hands-on section follows the same rhythm:

1. We introduce the topic together and connect it to the code you are about to change.
2. You open the starter project and inspect the code that matters for the lesson.
3. You pause for a timed challenge and implement the ideas that were just taught.
4. We review attendee approaches together, including tradeoffs, questions, and common mistakes.
5. We walk through the solution step by step as a group, re-iterating the key async/await concepts along the way.
6. You compare your implementation with the completed sample and ask any remaining questions while the context is fresh.

Most coding challenges are designed for 30 to 45 minutes. Shorter investigation challenges in the .NET Internals section focus on debugger observations and discussion rather than large code changes.

## Schedule

This workshop is designed for two 7-hour days. The active material is approximately 12 to 13.5 hours, with the remaining time reserved for setup drift, breaks, attendee questions, and deeper review when a topic needs more time.

### Day 1

1. Setup verification and workshop orientation.
2. Thread switching and compiler-generated code lecture.
3. Correcting common async/await mistakes lecture.
4. Correcting common async/await mistakes challenge.
5. Group review of attendee approaches.
6. Step-by-step solution walkthrough and Q&A.

### Day 2

1. Custom `Task` implementation lecture.
2. Custom `Task` implementation challenge.
3. Group review of attendee approaches.
4. Step-by-step solution walkthrough and Q&A.
5. .NET Internals investigations: `ThreadStatic`, `Principal`, `ExecutionContext`, and `SynchronizationContext`.
6. Final review and Q&A.

## Agenda

0. [Install Prerequisites](https://github.com/TheCodeTraveler/BecomeAnExpertWithAsyncAwait/tree/main/0.%20Prerequisites#0-install-prerequisites)
1. [(Presentation) Thread Switching + Compiler Generated Code](https://github.com/TheCodeTraveler/BecomeAnExpertWithAsyncAwait/blob/main/1.%20Thread%20Switching%20and%20Compiler%20Generatoed%20Code/ThreadSwitchingAndCompilerGeneratedCode.pptx)
2. [(Code) Correct Common Async Await Mistakes](https://github.com/TheCodeTraveler/BecomeAnExpertWithAsyncAwait/tree/main/2.%20Correcting%20Common%20Async%20Await%20Mistakes)
3. [(Code) Creating Custom Implementation of Task](https://github.com/TheCodeTraveler/BecomeAnExpertWithAsyncAwait/tree/main/3.%20Creating%20Custom%20Implementation%20of%20Task)
4. [(Presentation/Code) .NET Internals](https://github.com/TheCodeTraveler/BecomeAnExpertWithAsyncAwait/tree/main/4.%20.NET%20Internals)

## Additional Resources

Join me in these [DomeTrain](https://dometrain.com) courses where we'll learn everything you need to know to master asynchronous programming using async await in C# and .NET

[![Asynchronous Programming](https://github.com/user-attachments/assets/e3ce2f9b-7fb5-4103-9b00-46d1aea5c977)](https://dometrain.com/course/from-zero-to-hero-asynchronous-programming-in-csharp/)

[![Parallel Programming](https://github.com/user-attachments/assets/8567ec85-83d2-493b-bd9e-0a6dd42af60d)](https://dometrain.com/course/from-zero-to-hero-parallel-programming-in-csharp/)

[![Maui Getting Started](https://github.com/user-attachments/assets/d6de6109-476a-4fd7-8bbc-d24b0df207ed)](https://dometrain.com/course/getting-started-dotnet-maui/)

[![Maui Deep Dive](https://github.com/user-attachments/assets/d3194a4d-508e-44f2-8552-e86a2434c6b5)](https://dometrain.com/course/deep-dive-dotnet-maui/)
