namespace CoffeeShop;

public sealed class Step1PrintReceipt : WorkshopStep
{
	public override int Number => 1;

	public override string Title => "CustomTask.Run(), Wait() and IsCompleted";

	public override string Scenario => "Print the receipt";

	public override string Story => "The receipt printer's SDK calls the till synchronously and needs the finished receipt before its callback returns. "
		+ "Rendering the receipt is CPU-bound, so the till starts it on the thread pool with CustomTask.Run(), opens the cash drawer while it renders, then blocks with Wait() only because the callback must return synchronously. "
		+ "Calling Run() and then Wait() with nothing in between would only waste a thread, the same blocking wait you removed from the Top stories page in Correcting Common Async Await Mistakes. "
		+ "Run() completes its CustomTask with SetResult() or SetException(), so implement those two first.";

	public override string TaskEquivalent => "Task.Run(), Task.Wait() and Task.IsCompleted";

	public override string TimeoutHint => "Wait() is probably blocking on a CustomTask that is never completed. Check that Run() completes its CustomTask, and that SetResult() runs the continuation Wait() registered.";

	public override Task Run(StepContext context)
	{
		var report = context.Report;
		var receipt = new Receipt(new Order("Ada", "Flat white", 18));

		// Thread Ids differ between machines and runs, so compare against the thread that called Run(), captured here
		var callingThreadId = Environment.CurrentManagedThreadId;
		var renderThreadId = 0;
		var renderedOnThreadPoolThread = false;

		report.Log("The receipt printer asked the till for Ada's receipt. Rendering it with CustomTask.Run()");

		// Run() queues the action to the thread pool and returns the CustomTask immediately,
		// so the receipt renders on a thread pool thread, not on the thread that called Run().
		var rendering = CustomTask.Run(() =>
		{
			Volatile.Write(ref renderThreadId, Environment.CurrentManagedThreadId);
			Volatile.Write(ref renderedOnThreadPoolThread, Thread.CurrentThread.IsThreadPoolThread);

			report.Log("Rendering the receipt lines and the QR code");
			receipt.Render();
			report.Log("The receipt is rendered");
		});

		// Rendering takes about 500 ms, so Run() has returned long before the receipt is ready
		report.Expect("Run() returns before the receipt is rendered", false, receipt.IsRendered);
		report.Expect("IsCompleted right after Run()", false, rendering.IsCompleted);

		// The calling thread is free while the receipt renders, so it opens the cash drawer in the meantime
		report.Log("Opening the cash drawer while the receipt renders");
		OpenCashDrawer();
		report.Expect("The cash drawer opened while the receipt was still rendering", false, receipt.IsRendered);

		// The printer's callback has to return the finished receipt, so now, and only now, there is nothing left to do but block.
		// Wait() blocks the calling thread until the CustomTask completes, so the next line cannot run before the receipt is rendered.
		report.Log("Waiting for the receipt with Wait()");
		rendering.Wait();
		report.Log("Wait() returned");

		report.Expect("Wait() returns only after the receipt is rendered", true, receipt.IsRendered);

		var actualRenderThreadId = Volatile.Read(ref renderThreadId);
		var actualRenderedOnThreadPoolThread = Volatile.Read(ref renderedOnThreadPoolThread);
		report.Expect(
			"The receipt rendered on a thread pool thread, not the thread that called Run()",
			$"a thread pool thread, any Thread Id except {callingThreadId}",
			actualRenderThreadId is 0 ? "the action never ran" : $"Thread Id {actualRenderThreadId}, {(actualRenderedOnThreadPoolThread ? "a thread pool thread" : "not a thread pool thread")}",
			actualRenderThreadId != 0 && actualRenderThreadId != callingThreadId && actualRenderedOnThreadPoolThread);

		// Wait() does not switch threads: the calling thread was blocked, and now it is unblocked
		report.Expect("Wait() returns on the thread that called it", callingThreadId, Environment.CurrentManagedThreadId);
		report.Expect("IsCompleted after Wait()", true, rendering.IsCompleted);

		report.Log("The receipt printer has Ada's receipt");

		return Task.CompletedTask;
	}

	// The cash drawer's SDK is synchronous too: it pulses the drawer's latch and returns about 100 ms later
	static void OpenCashDrawer() => Thread.Sleep(TimeSpan.FromMilliseconds(100));
}