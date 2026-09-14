namespace CoffeeShop;

public sealed class Step4PullShot : WorkshopStep
{
	public override int Number => 4;

	public override string Title => "SetResult()";

	public override string Scenario => "Wrap the espresso machine's events";

	public override string Story => "The espresso machine's SDK has no Task anywhere: you start a shot, and it raises ShotPulled when the barista's shot is ready. "
		+ "PullShot() adapts that event to await by creating a CustomTask that runs no action, and completing it with SetResult() when the event is raised. "
		+ "That is exactly what TaskCompletionSource does for Task.";

	public override string TaskEquivalent => "TaskCompletionSource.SetResult() and TaskCompletionSource.Task";

	public override string TimeoutHint => "The await never resumed after the shot was pulled. Check that SetResult() marks the CustomTask as completed and queues every stored continuation.";

	public override BaristaAction BaristaAction => BaristaAction.FinishShot;

	public override async Task Run(StepContext context)
	{
		var report = context.Report;
		var machine = context.EspressoMachine;

		// A CustomTask does not have to run an action. You can create one and complete it yourself when something else finishes.
		var unusedTask = new CustomTask();
		report.Expect("A new CustomTask() that has no action to run is not completed", false, unusedTask.IsCompleted);

		// Handlers run in the order they subscribed, so this one records the event before PullShot()'s handler calls SetResult()
		var shotPulledRaised = false;
		var recordShotPulled = new EventHandler((_, _) => Volatile.Write(ref shotPulledRaised, true));
		machine.ShotPulled += recordShotPulled;

		var espresso = PullShot(machine, "Double espresso", report);
		report.Expect("IsCompleted while the shot is brewing", false, espresso.IsCompleted);

		// This await registers its continuation on the CustomTask. It resumes only after SetResult() is called.
		report.Log("Waiting for the barista to press Shot is ready");
		await espresso;
		report.Log("The await resumed: the double espresso is ready");

		machine.ShotPulled -= recordShotPulled;

		report.Expect("await resumes only after the machine raised ShotPulled", true, Volatile.Read(ref shotPulledRaised));
		report.Expect("IsCompleted after the await", true, espresso.IsCompleted);

		// A buggy SDK can raise ShotPulled twice. That is why PullShot() unsubscribes before calling SetResult(),
		// and it is why TaskCompletionSource.SetResult() throws here while TrySetResult() exists.
		try
		{
			espresso.SetResult();
			report.Expect("Completing the CustomTask a second time throws", nameof(InvalidOperationException), "no exception", false);
		}
		catch (InvalidOperationException)
		{
			report.Expect("Completing the CustomTask a second time throws", nameof(InvalidOperationException), nameof(InvalidOperationException));
		}
		catch (Exception e) when (e is not NotImplementedException)
		{
			report.Expect("Completing the CustomTask a second time throws", nameof(InvalidOperationException), e.GetType().Name);
		}
	}

	// The TaskCompletionSource pattern: adapt an event-based API to something you can await
	static CustomTask PullShot(EspressoMachine machine, string drink, StepReport report)
	{
		var shot = new CustomTask();

		machine.ShotPulled += OnShotPulled;
		machine.StartShot(drink);
		report.Log($"Started pulling a {drink.ToLowerInvariant()}");

		return shot;

		void OnShotPulled(object? sender, EventArgs e)
		{
			// Unsubscribe first, so this handler can never complete the same CustomTask twice
			machine.ShotPulled -= OnShotPulled;

			report.Log("The machine raised ShotPulled. Completing the CustomTask with SetResult()");
			shot.SetResult();
		}
	}
}