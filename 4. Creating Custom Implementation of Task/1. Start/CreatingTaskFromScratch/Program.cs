using CreatingTaskFromScratch;

// This program uses every public member of CustomTask, one step at a time.
// Watch the Thread Ids: they show when code runs on the main thread, when it moves to a thread pool thread, and when a thread is blocked.
Console.WriteLine($"The program started on the main thread. Thread Id: {Environment.CurrentManagedThreadId}");
Console.WriteLine();

// Step 1: CustomTask.Run(), Wait() and IsCompleted
Console.WriteLine("Step 1: CustomTask.Run(), Wait() and IsCompleted");

// Run() queues the action to the thread pool and returns immediately, so the action runs on a thread pool thread, not the main thread.
CustomTask runTask = CustomTask.Run(() => Console.WriteLine($"   The Run() action is running on a thread pool thread. Thread Id: {Environment.CurrentManagedThreadId}"));

// Wait() blocks the calling thread until the CustomTask completes, so the next line cannot run before the action has finished.
runTask.Wait();

// Wait() does not switch threads: the main thread was blocked, and now it is unblocked.
Console.WriteLine($"   Wait() returned on the same thread that called it. Thread Id: {Environment.CurrentManagedThreadId}, IsCompleted: {runTask.IsCompleted}");
Console.WriteLine();

// Step 2: CustomTask.Delay() and ContinueWith()
Console.WriteLine("Step 2: CustomTask.Delay() and ContinueWith()");

// Delay() starts a timer and returns immediately. No thread is blocked while the time passes, so IsCompleted is still false.
CustomTask delayTask = CustomTask.Delay(TimeSpan.FromSeconds(1));
Console.WriteLine($"   Delay() returned before the delay finished. IsCompleted: {delayTask.IsCompleted}");

// ContinueWith() stores an action to run after delayTask completes, and returns a new CustomTask that completes when that action finishes.
// Calling ContinueWith() on the returned CustomTask builds a chain: each link runs only after the link before it has completed.
CustomTask continuationChain = delayTask
	.ContinueWith(() => Console.WriteLine($"   First continuation: runs after the delay completes. Thread Id: {Environment.CurrentManagedThreadId}"))
	.ContinueWith(() => Console.WriteLine($"   Second continuation: runs after the first continuation completes. Thread Id: {Environment.CurrentManagedThreadId}"));

// The timer has not fired yet, so neither continuation has run. Block until the last link in the chain completes.
continuationChain.Wait();
Console.WriteLine();

// Step 3: await, which uses GetAwaiter()
Console.WriteLine("Step 3: await, which uses GetAwaiter()");
Console.WriteLine($"   Awaiting CustomTask.Delay(). Thread Id: {Environment.CurrentManagedThreadId}");

// The await keyword compiles because CustomTask has a GetAwaiter() method.
// The compiler checks the awaiter's IsCompleted. The delay has not finished, so instead of blocking a thread like Wait() does,
// the rest of this program is registered as a continuation with the awaiter's OnCompleted().
await CustomTask.Delay(TimeSpan.FromSeconds(1));

// A console app has no SynchronizationContext, so nothing sends the continuation back to the main thread. It resumes on a thread pool thread.
Console.WriteLine($"   Resumed on a thread pool thread after the delay. Thread Id: {Environment.CurrentManagedThreadId}");

// runTask completed in Step 1, so the awaiter's IsCompleted returns true.
// The compiler skips OnCompleted(), calls GetResult() right away, and the code after the await keeps running on the same thread.
int threadIdBeforeAwait = Environment.CurrentManagedThreadId;
await runTask;
Console.WriteLine($"   Awaiting an already completed CustomTask does not switch threads. Thread Id before await: {threadIdBeforeAwait}, after await: {Environment.CurrentManagedThreadId}");
Console.WriteLine();

// Step 4: SetResult()
Console.WriteLine("Step 4: SetResult()");

// A CustomTask does not have to run an action. You can create one and complete it yourself when something else finishes,
// which is what TaskCompletionSource does for Task.
CustomTask manuallyCompletedTask = new();
Console.WriteLine($"   Created a CustomTask that has no action to run. IsCompleted: {manuallyCompletedTask.IsCompleted}");

CustomTask setResultTask = CustomTask.Delay(TimeSpan.FromSeconds(1)).ContinueWith(() =>
{
	Console.WriteLine($"   The delay completed, so the continuation is calling SetResult(). Thread Id: {Environment.CurrentManagedThreadId}");
	manuallyCompletedTask.SetResult();
});

// This await registers its continuation on manuallyCompletedTask. It resumes only after SetResult() is called.
await manuallyCompletedTask;
Console.WriteLine($"   The await resumed because SetResult() completed the CustomTask. IsCompleted: {manuallyCompletedTask.IsCompleted}");

// ContinueWith() returned setResultTask, which completes when the continuation that called SetResult() finishes.
// Wait for it too, so a CustomTask is never left running unobserved.
setResultTask.Wait();
Console.WriteLine();

// Step 5: SetException()
Console.WriteLine("Step 5: SetException()");

// SetException() completes a CustomTask as failed. It does not throw. The exception is stored until someone waits on or awaits the CustomTask.
CustomTask failedTask = new();
failedTask.SetException(new InvalidOperationException("This exception was stored by SetException()"));
Console.WriteLine($"   SetException() completed the CustomTask without throwing. IsCompleted: {failedTask.IsCompleted}");

try
{
	// await calls GetResult() on the awaiter, which rethrows the stored exception here, on the thread that is awaiting.
	await failedTask;
}
catch (InvalidOperationException e)
{
	Console.WriteLine($"   await rethrew the stored exception: {e.Message}");
}

try
{
	// When the Run() action throws, Run() catches the exception on the thread pool thread and stores it with SetException().
	CustomTask.Run(() => throw new InvalidOperationException("This exception was thrown inside the Run() action")).Wait();
}
catch (InvalidOperationException e)
{
	// Wait() rethrows the exception without resetting its stack trace.
	// The top frame still points to the lambda in Program.cs that threw it. Rethrowing with `throw exception;` would make it point to Wait() instead.
	Console.WriteLine($"   Wait() rethrew the exception from the Run() action: {e.Message}");
	Console.WriteLine($"   The stack trace still starts where the exception was thrown: {e.StackTrace?.Split(Environment.NewLine)[0].Trim()}");
}