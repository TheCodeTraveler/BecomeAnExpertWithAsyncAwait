using CreatingTaskFromScratch;

// This program uses every public member of CustomTask, one step at a time.
// Every line it prints ends with the result you should expect, so compare the two as you implement CustomTask.
// Thread Ids differ between machines and runs, so the expected results describe which thread the code should be running on.
var mainThreadId = Environment.CurrentManagedThreadId;
Console.WriteLine($"The program started on the main thread. Main thread Id: {mainThreadId}");
Console.WriteLine();

// Step 1: CustomTask.Run(), Wait() and IsCompleted
Console.WriteLine("Step 1: CustomTask.Run(), Wait() and IsCompleted");

// Run() queues the action to the thread pool and returns the Task immediately, so the action runs on a thread pool thread, not the main thread.
var runTask = CustomTask.Run(() => Console.WriteLine($"   The Run() action is running. Thread Id: {Environment.CurrentManagedThreadId}. Expected: a thread pool thread, so any Thread Id except {mainThreadId}"));

// Wait() blocks the calling thread until the CustomTask completes, so the next line cannot run before the action has finished.
runTask.Wait();

// Wait() does not switch threads: the main thread was blocked, and now it is unblocked.
Console.WriteLine($"   Wait() returned. Thread Id: {Environment.CurrentManagedThreadId}. Expected: the main thread, Thread Id {mainThreadId}");
Console.WriteLine($"   IsCompleted: {runTask.IsCompleted}. Expected: True");
Console.WriteLine();

// Step 2: CustomTask.Delay() and ContinueWith()
Console.WriteLine("Step 2: CustomTask.Delay() and ContinueWith()");

// Delay() starts a timer and returns immediately. No thread is blocked while the time passes.
var delayTask = CustomTask.Delay(TimeSpan.FromSeconds(1));
Console.WriteLine($"   Delay() returned. IsCompleted: {delayTask.IsCompleted}. Expected: False, because the 1 second delay has not finished yet");

// ContinueWith() stores an action to run after delayTask completes, and returns a new CustomTask that completes when that action finishes.
// Calling ContinueWith() on the returned CustomTask builds a chain: each link runs only after the link before it has completed.
var continuationChain = delayTask
	.ContinueWith(() => Console.WriteLine($"   The first continuation is running. Thread Id: {Environment.CurrentManagedThreadId}. Expected: after a 1 second pause, on a thread pool thread, so any Thread Id except {mainThreadId}"))
	.ContinueWith(() => Console.WriteLine($"   The second continuation is running. Thread Id: {Environment.CurrentManagedThreadId}. Expected: after the first continuation, on a thread pool thread, so any Thread Id except {mainThreadId}"));

// The timer has not fired yet, so neither continuation has run. Block the main thread until the last link in the chain completes.
continuationChain.Wait();
Console.WriteLine($"   Wait() returned after the whole chain completed. Thread Id: {Environment.CurrentManagedThreadId}. Expected: the main thread, Thread Id {mainThreadId}");
Console.WriteLine();

// Step 3: await, which uses GetAwaiter()
Console.WriteLine("Step 3: await, which uses GetAwaiter()");
Console.WriteLine($"   About to await CustomTask.Delay(). Thread Id: {Environment.CurrentManagedThreadId}. Expected: the main thread, Thread Id {mainThreadId}");

// The await keyword compiles because CustomTask has a GetAwaiter() method.
// The compiler checks the awaiter's IsCompleted. The delay has not finished, so instead of blocking a thread like Wait() does,
// the rest of this program is registered as a continuation with the awaiter's OnCompleted().
await CustomTask.Delay(TimeSpan.FromSeconds(1));

// A console app has no SynchronizationContext, so nothing sends the continuation back to the main thread. It resumes on a thread pool thread.
Console.WriteLine($"   Resumed after the await. Thread Id: {Environment.CurrentManagedThreadId}. Expected: after a 1 second pause, on a thread pool thread, so any Thread Id except {mainThreadId}");

// runTask completed in Step 1, so the awaiter's IsCompleted returns true.
// The compiler skips OnCompleted(), calls GetResult() right away, and the code after the await keeps running on the same thread.
var threadIdBeforeAwait = Environment.CurrentManagedThreadId;
await runTask;
Console.WriteLine($"   Awaited the already completed runTask. Thread Id before await: {threadIdBeforeAwait}, after await: {Environment.CurrentManagedThreadId}. Expected: the same Thread Id before and after");
Console.WriteLine();

// Step 4: SetResult()
Console.WriteLine("Step 4: SetResult()");

// A CustomTask does not have to run an action. You can create one and complete it yourself when something else finishes,
// which is what TaskCompletionSource does for Task.
var manuallyCompletedTask = new CustomTask();
Console.WriteLine($"   Created a CustomTask that has no action to run. IsCompleted: {manuallyCompletedTask.IsCompleted}. Expected: False");

var setResultTask = CustomTask.Delay(TimeSpan.FromSeconds(1)).ContinueWith(() =>
{
	Console.WriteLine($"   The continuation is calling SetResult(). Thread Id: {Environment.CurrentManagedThreadId}. Expected: after a 1 second pause, on a thread pool thread, so any Thread Id except {mainThreadId}");
	manuallyCompletedTask.SetResult();
});

// This await registers its continuation on manuallyCompletedTask. It resumes only after SetResult() is called.
await manuallyCompletedTask;
Console.WriteLine($"   The await resumed. IsCompleted: {manuallyCompletedTask.IsCompleted}. Expected: True, printed after SetResult() was called");

// ContinueWith() returned setResultTask, which completes when the continuation that called SetResult() finishes.
// Wait for it too, so a CustomTask is never left running unobserved.
setResultTask.Wait();
Console.WriteLine();

// Step 5: SetException()
Console.WriteLine("Step 5: SetException()");

// SetException() completes a CustomTask as failed. It does not throw. The exception is stored until someone waits on or awaits the CustomTask.
var failedTask = new CustomTask();
failedTask.SetException(new InvalidOperationException("This exception was stored by SetException()"));
Console.WriteLine($"   SetException() returned without throwing. IsCompleted: {failedTask.IsCompleted}. Expected: True");

try
{
	// await calls GetResult() on the awaiter, which rethrows the stored exception here, on the thread that is awaiting.
	await failedTask;

	// This line should never print, because the await above should throw.
	Console.WriteLine("   Unexpected: await did not rethrow the exception stored by SetException()");
}
catch (InvalidOperationException e)
{
	Console.WriteLine($"   await rethrew an exception. Message: {e.Message}. Expected: This exception was stored by SetException()");
}

try
{
	// When the Run() action throws, Run() catches the exception on the thread pool thread and stores it with SetException().
	CustomTask.Run(() => throw new InvalidOperationException("This exception was thrown inside the Run() action")).Wait();

	// This line should never print, because Wait() above should throw.
	Console.WriteLine("   Unexpected: Wait() did not rethrow the exception thrown inside the Run() action");
}
catch (InvalidOperationException e)
{
	// Wait() rethrows the exception without resetting its stack trace.
	// The top frame still points to the lambda in Program.cs that threw it. Rethrowing with `throw exception;` would make it point to Wait() instead.
	Console.WriteLine($"   Wait() rethrew an exception. Message: {e.Message}. Expected: This exception was thrown inside the Run() action");
	Console.WriteLine($"   The top of the stack trace: {e.StackTrace?.Split(Environment.NewLine)[0].Trim()}. Expected: the lambda in Program.cs that threw the exception, not CustomTask.Wait()");
}

Console.WriteLine();
Console.WriteLine("All five steps finished. If every line above matches its Expected result, your CustomTask passes Program.cs.");