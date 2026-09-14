using System.Diagnostics;

namespace CoffeeShop;

public sealed class Step3MobileOrder : WorkshopStep
{
	public override int Number => 3;

	public override string Title => "await, which uses GetAwaiter()";

	public override string Scenario => "Prepare a mobile order without blocking the till";

	public override string Story => "Mobile orders arrive while customers are standing at the till. "
		+ "PrepareMobileOrder() is an async method that awaits CustomTask.Delay() while the beans are ground and tamped, so the till gets its thread back at the first await instead of blocking the way Wait() does. "
		+ "await compiles because CustomTask has a GetAwaiter() method.";

	public override string TaskEquivalent => "await Task, which uses Task.GetAwaiter()";

	public override string TimeoutHint => "An await never resumed. Check that GetAwaiter() returns a CustomTaskAwaiter for this CustomTask, and that SetResult() runs the continuation that OnCompleted() registered through ContinueWith().";

	public override async Task Run(StepContext context)
	{
		var report = context.Report;

		report.Log("A mobile order arrived while the till is busy");

		// PrepareMobileOrder() runs synchronously until its first await. The delay has not finished there,
		// so the method returns an incomplete Task to the till right away.
		var mobileOrder = PrepareMobileOrder(report);
		report.Expect("PrepareMobileOrder() returns to the till at its first await", false, mobileOrder.IsCompleted);
		report.Log("The till is free to take the next order while the mobile order waits");

		var (resumedAfter, resumedOnThreadPoolThread) = await mobileOrder.ConfigureAwait(false);

		report.Expect(
			"PrepareMobileOrder() resumes after the 1 second delay",
			"at least 900 ms after the await",
			$"{resumedAfter.TotalMilliseconds:0} ms after the await",
			resumedAfter >= TimeSpan.FromMilliseconds(900));

		// Nothing sends the continuation back to the thread that started the await. It resumes on a thread pool thread.
		report.Expect("PrepareMobileOrder() resumes on a thread pool thread", true, resumedOnThreadPoolThread);

		// A pastry that was already warmed: this CustomTask has completed before anyone awaits it
		var warmPastry = CustomTask.Run(() => report.Log("Warming a croissant"));
		warmPastry.Wait();

		var callerReturned = 0;
		var awaitRanSynchronously = AwaitCompletedTask(warmPastry, () => Volatile.Read(ref callerReturned) is 0);
		Volatile.Write(ref callerReturned, 1);

		report.Expect("Awaiting an already completed CustomTask keeps running synchronously on the same thread", true, await awaitRanSynchronously.ConfigureAwait(false));

		var synchronizationContextAfterAwait = await ObserveSynchronizationContext().ConfigureAwait(false);

		// This is an observation, not a check: capturing a SynchronizationContext is not something CustomTask has to do.
		// Because it does not, Blazor code must use InvokeAsync(...) after awaiting a CustomTask, the same as after ConfigureAwait(false).
		report.Log($"SynchronizationContext after awaiting a CustomTask: {synchronizationContextAfterAwait}. CustomTask does not send the continuation back to it, so Blazor code must use InvokeAsync(...) afterwards");
	}

	static async Task<(TimeSpan ResumedAfter, bool ResumedOnThreadPoolThread)> PrepareMobileOrder(StepReport report)
	{
		var stopwatch = Stopwatch.StartNew();

		report.Log("Grinding and tamping the beans for the mobile order");

		// The compiler checks the awaiter's IsCompleted. The delay has not finished, so instead of blocking a thread like Wait() does,
		// the rest of this method is registered as a continuation with the awaiter's OnCompleted().
		await CustomTask.Delay(TimeSpan.FromSeconds(1));

		report.Log("The mobile order resumed after the await");

		return (stopwatch.Elapsed, Thread.CurrentThread.IsThreadPoolThread);
	}

	// Returns true only when the code after the await ran on the same thread, before this method returned to its caller
	static async Task<bool> AwaitCompletedTask(CustomTask completedTask, Func<bool> isCallerStillWaitingForReturn)
	{
		var threadIdBeforeAwait = Environment.CurrentManagedThreadId;

		// The awaiter's IsCompleted returns true, so the compiler skips OnCompleted(),
		// calls GetResult() right away, and the code after the await keeps running on the same thread.
		await completedTask;

		return Environment.CurrentManagedThreadId == threadIdBeforeAwait && isCallerStillWaitingForReturn();
	}

	static async Task<string> ObserveSynchronizationContext()
	{
		// A stand-in for Blazor's renderer context. Setting it inside this async method is safe:
		// when the method returns to its caller, the async method builder restores the caller's own context.
		var tillContext = new SynchronizationContext();
		SynchronizationContext.SetSynchronizationContext(tillContext);

		await CustomTask.Delay(TimeSpan.FromMilliseconds(100));

		var current = SynchronizationContext.Current;

		return current is null
			? "none"
			: ReferenceEquals(current, tillContext) ? "the till's context" : current.GetType().Name;
	}
}