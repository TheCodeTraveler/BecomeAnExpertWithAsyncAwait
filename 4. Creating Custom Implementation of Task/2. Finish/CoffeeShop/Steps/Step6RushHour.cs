namespace CoffeeShop;

public sealed class Step6RushHour : WorkshopStep
{
	const int _chainLength = 10_000;

	// A step must never block forever on a CustomTask that might be broken, so every wait here gives up after this long
	static readonly TimeSpan _gracePeriod = TimeSpan.FromSeconds(2);

	// Which customer the code is currently serving. AsyncLocal values travel with the ExecutionContext.
	static readonly AsyncLocal<string?> _currentCustomer = new();

	public override int Number => 6;

	public override string Title => "Rush hour: many waiters, long chains and ExecutionContext";

	public override string Scenario => "Rush hour";

	public override string Story => "At rush hour two customers wait on the same order, the loyalty app chains thousands of small updates, and the logging code needs to know which customer it is serving. "
		+ "A CustomTask has to run every continuation registered on it, queue continuations instead of running them inline so long chains cannot overflow the stack, "
		+ "and run each continuation inside the ExecutionContext that was captured when it was registered.";

	public override string TaskEquivalent => "Task.ContinueWith() with many continuations, and ExecutionContext.Capture() / ExecutionContext.Run()";

	public override string TimeoutHint => "A continuation is probably blocked or never ran. Check that SetResult() queues every stored continuation to the thread pool.";

	// Not run on startup: a CustomTask that runs continuations inline can still crash the app here
	public override bool VerifyOnStartup => false;

	public override Task Run(StepContext context)
	{
		var report = context.Report;

		var queuesContinuations = CheckContinuationsAreQueued(report);
		CheckTwoCustomersWaitingOnOneOrder(report);

		if (queuesContinuations)
		{
			CheckLongChain(report);
		}
		else
		{
			report.Expect(
				$"A {_chainLength:N0}-link ContinueWith chain completes",
				"Completed",
				"Skipped: continuations run inline, so this chain would overflow the stack and crash the app",
				false);
		}

		CheckExecutionContextFlow(report);

		return Task.CompletedTask;
	}

	static bool CheckContinuationsAreQueued(StepReport report)
	{
		report.Log("Checking that SetResult() queues continuations instead of running them inside SetResult()");

		var orderReady = new CustomTask();
		using var setResultReturned = new ManualResetEventSlim();
		var startedAfterSetResultReturned = false;

		var continuation = orderReady.ContinueWith(() =>
		{
			// A queued continuation can start before SetResult() returns, so wait, briefly, for SetResult() to return.
			// A continuation running inline is inside SetResult(), so the signal cannot arrive and the wait gives up.
			if (setResultReturned.Wait(_gracePeriod))
				Volatile.Write(ref startedAfterSetResultReturned, true);
		});

		orderReady.SetResult();
		setResultReturned.Set();

		SpinWait.SpinUntil(() => continuation.IsCompleted, _gracePeriod);

		var queuesContinuations = Volatile.Read(ref startedAfterSetResultReturned);
		report.Expect("SetResult() queues continuations instead of running them inline", true, queuesContinuations);

		return queuesContinuations;
	}

	static void CheckTwoCustomersWaitingOnOneOrder(StepReport report)
	{
		report.Log("Two customers are waiting on the same batch of cold brew");

		// One CustomTask can have many continuations. Both are stored while the batch is still brewing, and both must run.
		var coldBrew = CustomTask.Delay(TimeSpan.FromMilliseconds(500));
		var customersServed = 0;

		var ada = coldBrew.ContinueWith(() => Interlocked.Increment(ref customersServed));
		var grace = coldBrew.ContinueWith(() => Interlocked.Increment(ref customersServed));

		SpinWait.SpinUntil(() => ada.IsCompleted && grace.IsCompleted, TimeSpan.FromMilliseconds(500) + _gracePeriod);

		var served = Volatile.Read(ref customersServed);
		report.Expect("Both customers waiting on the same order are served", "2 of 2", $"{served} of 2", served is 2);
	}

	static void CheckLongChain(StepReport report)
	{
		report.Log($"The loyalty app chains {_chainLength:N0} point updates onto one order");

		// Every link is stored while the first CustomTask is still waiting, so completing it starts the whole chain.
		// If each SetResult() ran the next link inline, this chain would be 10,000 calls deep and overflow the stack.
		var linksRun = 0;
		var link = CustomTask.Delay(TimeSpan.FromMilliseconds(500));

		for (var i = 0; i < _chainLength; i++)
		{
			link = link.ContinueWith(() => Interlocked.Increment(ref linksRun));
		}

		var lastLink = link;
		SpinWait.SpinUntil(() => lastLink.IsCompleted, TimeSpan.FromMilliseconds(500) + _gracePeriod);

		var actualLinksRun = Volatile.Read(ref linksRun);
		var isLastLinkCompleted = lastLink.IsCompleted;
		report.Expect(
			$"A {_chainLength:N0}-link ContinueWith chain completes",
			$"{_chainLength:N0} links ran, and the last link is completed",
			$"{actualLinksRun:N0} links ran, and the last link is {(isLastLinkCompleted ? "completed" : "not completed")}",
			actualLinksRun is _chainLength && isLastLinkCompleted);
	}

	static void CheckExecutionContextFlow(StepReport report)
	{
		report.Log("Serving Ada. Her order-ready notification is registered while she is the current customer");

		var orderReady = new CustomTask();
		var customerSeenWithFlow = "the continuation never ran";
		var customerSeenWithoutFlow = "the continuation never ran";

		_currentCustomer.Value = "Ada";

		// ContinueWith() captures the ExecutionContext when the continuation is registered, so this continuation should see Ada
		var withFlow = orderReady.ContinueWith(() => Volatile.Write(ref customerSeenWithFlow, _currentCustomer.Value ?? "no customer"));

		// SuppressFlow() stops ExecutionContext.Capture() from capturing anything, so this continuation should see no customer.
		// It returns a thread-affine AsyncFlowControl: create the CustomTask inside the using block, and wait for it after the block.
		CustomTask withoutFlow;
		using (ExecutionContext.SuppressFlow())
		{
			withoutFlow = orderReady.ContinueWith(() => Volatile.Write(ref customerSeenWithoutFlow, _currentCustomer.Value ?? "no customer"));
		}

		_currentCustomer.Value = null;

		// The order is completed while Grace is the current customer on another thread.
		// Neither continuation should see Grace: each one runs with the context captured when it was registered, never the completer's.
		var completer = CustomTask.Run(() =>
		{
			_currentCustomer.Value = "Grace";
			orderReady.SetResult();
		});

		SpinWait.SpinUntil(() => completer.IsCompleted && withFlow.IsCompleted && withoutFlow.IsCompleted, _gracePeriod);

		report.Expect("A continuation sees the customer from when it was registered", "Ada", Volatile.Read(ref customerSeenWithFlow));
		report.Expect("A continuation registered inside SuppressFlow() sees no customer, not the completer's", "no customer", Volatile.Read(ref customerSeenWithoutFlow));
	}
}