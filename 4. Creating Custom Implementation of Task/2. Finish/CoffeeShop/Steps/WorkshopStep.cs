namespace CoffeeShop;

// Every step is a coffee shop feature built on CustomTask.
// Run() uses the feature, logs what happens, and records an expected result for everything that should happen.
public abstract class WorkshopStep
{
	public abstract int Number { get; }

	// The CustomTask APIs this step uses
	public abstract string Title { get; }

	// The coffee shop feature, in a few words
	public abstract string Scenario { get; }

	// The situation in the coffee shop, and why this CustomTask API is the right tool for it
	public abstract string Story { get; }

	// The real .NET API this step's CustomTask API mirrors
	public abstract string TaskEquivalent { get; }

	// What is most likely waiting forever when this step does not finish in time
	public abstract string TimeoutHint { get; }

	// Which button the barista presses on the espresso machine while this step runs
	public virtual BaristaAction BaristaAction => BaristaAction.None;

	public virtual bool VerifyOnStartup => true;

	public string SourceFile => $"Steps/{GetType().Name}.cs";

	public abstract Task Run(StepContext context);
}