using System.Diagnostics;

namespace CoffeeShop;

public sealed class Step2BrewPourOver : WorkshopStep
{
	static readonly TimeSpan _brewTime = TimeSpan.FromSeconds(1);

	public override int Number => 2;

	public override string Title => "CustomTask.Delay() and ContinueWith()";

	public override string Scenario => "Brew a pour-over, then call the order";

	public override string Story => "A pour-over brews for a while, and nobody should stand still while it does. "
		+ "CustomTask.Delay() waits with a timer instead of blocking a thread, and ContinueWith() chains what happens next: call out the customer's name, then update the order-ready board. "
		+ "Each link runs only after the link before it has completed.";

	public override string TaskEquivalent => "Task.Delay() and Task.ContinueWith()";

	public override string TimeoutHint => "Either Delay() never completes its CustomTask, or the CustomTask returned by ContinueWith() is never completed, so Wait() on the last link blocks forever.";

	public override Task Run(StepContext context)
	{
		var report = context.Report;
		var stopwatch = Stopwatch.StartNew();

		var callOutElapsedMilliseconds = -1L;
		var callOutOnThreadPoolThread = false;
		var nameCalledOut = false;
		var nameWasCalledOutBeforeBoardUpdated = false;

		report.Log("Brewing Grace's pour-over");

		// Delay() starts a timer and returns immediately. No thread is blocked while the coffee brews.
		var pourOver = CustomTask.Delay(_brewTime);
		report.Expect("IsCompleted right after Delay(), because the 1 second brew has not finished yet", false, pourOver.IsCompleted);

		// ContinueWith() stores an action to run after pourOver completes, and returns a new CustomTask that completes when that action finishes
		var callOut = pourOver.ContinueWith(() =>
		{
			Volatile.Write(ref callOutElapsedMilliseconds, stopwatch.ElapsedMilliseconds);
			Volatile.Write(ref callOutOnThreadPoolThread, Thread.CurrentThread.IsThreadPoolThread);

			report.Log("The pour-over is ready. Calling out Grace's name");
			CallOutName();
			Volatile.Write(ref nameCalledOut, true);
		});

		report.Expect("ContinueWith() returns a new CustomTask, not the one it continues", false, ReferenceEquals(callOut, pourOver));

		// The brew is still inside its 1 second delay, so the continuation has not even started
		report.Expect("The CustomTask returned by ContinueWith() is not completed before its continuation runs", false, callOut.IsCompleted);

		// Calling ContinueWith() on the returned CustomTask builds a chain: the board updates only after the name has been called out
		var updateBoard = callOut.ContinueWith(() =>
		{
			Volatile.Write(ref nameWasCalledOutBeforeBoardUpdated, Volatile.Read(ref nameCalledOut));
			report.Log("Updating the order-ready board");
		});

		// The timer has not fired yet, so neither continuation has run. Block until the last link in the chain completes.
		report.Log("Waiting for the last link in the chain with Wait()");
		updateBoard.Wait();
		report.Log("Wait() returned after the whole chain completed");

		var actualCallOutElapsedMilliseconds = Volatile.Read(ref callOutElapsedMilliseconds);
		report.Expect(
			"The first continuation runs after the 1 second brew",
			"at least 900 ms after Delay()",
			actualCallOutElapsedMilliseconds < 0 ? "it never ran" : $"{actualCallOutElapsedMilliseconds} ms after Delay()",
			actualCallOutElapsedMilliseconds >= 900);

		report.Expect("The first continuation runs on a thread pool thread", true, Volatile.Read(ref callOutOnThreadPoolThread));
		report.Expect("The order-ready board updates only after the name was called out", true, Volatile.Read(ref nameWasCalledOutBeforeBoardUpdated));
		report.Expect("IsCompleted of the last link after Wait()", true, updateBoard.IsCompleted);

		return Task.CompletedTask;
	}

	// The store speaker's SDK is synchronous: announcing a name takes about 300 ms
	static void CallOutName() => Thread.Sleep(TimeSpan.FromMilliseconds(300));
}