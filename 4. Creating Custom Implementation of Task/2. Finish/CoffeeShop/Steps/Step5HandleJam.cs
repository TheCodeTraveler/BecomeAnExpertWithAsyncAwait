using System.Diagnostics;

namespace CoffeeShop;

public sealed class Step5HandleJam : WorkshopStep
{
	public override int Number => 5;

	public override string Title => "SetException()";

	public override string Scenario => "Handle a jammed machine and an empty grinder";

	public override string Story => "Hardware fails. When the espresso machine raises Jammed, PullShot() completes its CustomTask as failed with SetException(), and the code awaiting the shot catches the machine's exception as if it had been thrown right there. "
		+ "The grinder fails too: when CoffeeGrinder.Grind() throws inside Run(), Wait() has to rethrow that exception without losing where it was thrown, or nobody can tell which part of the shop broke.";

	public override string TaskEquivalent => "TaskCompletionSource.SetException(), and ExceptionDispatchInfo.Throw() inside Task.Wait() and await";

	public override string TimeoutHint => "The await never resumed after the machine jammed. Check that SetException() completes the CustomTask the same way SetResult() does.";

	public override BaristaAction BaristaAction => BaristaAction.JamMachine;

	public override async Task Run(StepContext context)
	{
		var report = context.Report;
		var machine = context.EspressoMachine;

		// Part 1: the espresso machine jams

		// Handlers run in the order they subscribed, so this one keeps the machine's exception before PullShot()'s handler stores it
		EspressoMachineJammedException? raisedException = null;
		var recordJam = new EventHandler<MachineJammedEventArgs>((_, e) => Volatile.Write(ref raisedException, e.Exception));
		machine.Jammed += recordJam;

		using var setExceptionReturned = new ManualResetEventSlim();
		var cortado = PullShot(machine, "Cortado", report, setExceptionReturned);

		try
		{
			report.Log("Waiting for the cortado. The barista will press Machine jammed");

			// await calls GetResult() on the awaiter, which rethrows the stored exception here, on the thread that is awaiting
			await cortado;

			// This line should never run, because the await above should throw
			report.Expect("await rethrows the machine's exception", nameof(EspressoMachineJammedException), "no exception", false);
		}
		catch (EspressoMachineJammedException e)
		{
			var isSameException = ReferenceEquals(e, Volatile.Read(ref raisedException));
			report.Log("The await rethrew the machine's exception. Apologizing and remaking the cortado");
			report.Expect(
				"await rethrows the exact exception the machine raised",
				$"the same {nameof(EspressoMachineJammedException)}",
				isSameException ? $"the same {nameof(EspressoMachineJammedException)}" : $"a different {nameof(EspressoMachineJammedException)}",
				isSameException);
		}
		catch (Exception e) when (e is not NotImplementedException)
		{
			report.Expect("await rethrows the machine's exception", nameof(EspressoMachineJammedException), e.GetType().Name);
		}
		finally
		{
			machine.Jammed -= recordJam;
		}

		// The await can resume on another thread before the handler finishes, so give the handler a moment to report back
		report.Expect("SetException() returns without throwing", true, setExceptionReturned.Wait(TimeSpan.FromSeconds(2)));
		report.Expect("IsCompleted after SetException()", true, cortado.IsCompleted);

		// Part 2: the grinder runs out of beans
		var grinder = new CoffeeGrinder(gramsInHopper: 5);
		var order = new Order("Linus", "Cortado", 18);

		// The machine's pre-infusion hook is a synchronous callback, so it has to block until the beans are ground.
		// It still starts grinding first and warms the cup in the meantime, so the wait is not wasted.
		report.Log("Grinding 18 g of beans with CustomTask.Run() while the cup warms");

		// When the Run() action throws, Run() catches the exception on the thread pool thread and stores it with SetException()
		var grinding = CustomTask.Run(() => grinder.Grind(order));
		WarmCup();

		try
		{
			grinding.Wait();

			// This line should never run, because Wait() above should throw
			report.Expect("Wait() rethrows the grinder's exception", nameof(GrinderEmptyException), "no exception", false);
		}
		catch (GrinderEmptyException e)
		{
			report.Log("Wait() rethrew the grinder's exception. Asking someone to refill the hopper");
			report.Expect("Wait() rethrows the grinder's exception", nameof(GrinderEmptyException), e.GetType().Name);

			// Wait() should rethrow the exception without resetting its stack trace, so the trace still includes CoffeeGrinder.Grind(), where it was thrown.
			// Rethrowing with `throw exception;` would make the trace start inside CustomTask instead.
			var includesGrinder = new StackTrace(e).GetFrames().Any(static frame => frame.GetMethod()?.DeclaringType == typeof(CoffeeGrinder));
			report.Expect(
				"The stack trace still includes where the grinder threw",
				"includes CoffeeGrinder.Grind()",
				includesGrinder ? "includes CoffeeGrinder.Grind()" : "starts inside CustomTask",
				includesGrinder);
		}
		catch (Exception e) when (e is not NotImplementedException)
		{
			report.Expect("Wait() rethrows the grinder's exception", nameof(GrinderEmptyException), e.GetType().Name);
		}
	}

	static CustomTask PullShot(EspressoMachine machine, string drink, StepReport report, ManualResetEventSlim setExceptionReturned)
	{
		var shot = new CustomTask();

		machine.ShotPulled += OnShotPulled;
		machine.Jammed += OnJammed;
		machine.StartShot(drink);
		report.Log($"Started pulling a {drink.ToLowerInvariant()}");

		return shot;

		void OnShotPulled(object? sender, EventArgs e)
		{
			Unsubscribe();

			report.Log("The machine raised ShotPulled. Completing the CustomTask with SetResult()");
			shot.SetResult();
		}

		void OnJammed(object? sender, MachineJammedEventArgs e)
		{
			Unsubscribe();

			report.Log("The machine raised Jammed. Completing the CustomTask with SetException()");

			// SetException() completes the CustomTask as failed. It does not throw: the exception is stored until someone waits on or awaits the CustomTask.
			shot.SetException(e.Exception);
			setExceptionReturned.Set();
		}

		void Unsubscribe()
		{
			machine.ShotPulled -= OnShotPulled;
			machine.Jammed -= OnJammed;
		}
	}

	// Warming the cup takes about 100 ms
	static void WarmCup() => Thread.Sleep(TimeSpan.FromMilliseconds(100));
}