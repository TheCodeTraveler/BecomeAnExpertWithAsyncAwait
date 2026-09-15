using System.Globalization;
using System.Security.Claims;

namespace InternalsLab;

// Step 2: ExecutionContext
// RunAsync() plays the part of a console app's Main method: the Step 2 page starts it on a new dedicated thread, which acts as the main thread.
// That thread starts while ExecutionContext flow is suppressed, so it begins with default values, and nothing it assigns reaches the rest of this app.
// Every RecordThreadValues(...) call is one checkpoint on the Step 2 page. Predict whose values each one sees before you run it.
public sealed class ExecutionContextExperiment(CheckpointLog<ExecutionContextValues> log)
{
	static readonly AsyncLocal<string> _asyncLocalData = new();

	public async Task RunAsync()
	{
		// Assign data controlled by ExecutionContext
		CultureInfo.CurrentCulture = new CultureInfo("es-ES");
		Thread.CurrentPrincipal = new ClaimsPrincipal();
		_asyncLocalData.Value = "Initial Value";

		// Step 2: checkpoint 1. The main thread, right after assigning its values
		RecordThreadValues(1);

		var mainThreadExecutionContext = ExecutionContext.Capture() ?? throw new InvalidOperationException("ExecutionContext only null when suppressed");

		var thread = new Thread(() =>
		{
			// Try it: comment out these three assignments and apply the change with Hot Reload, then click Run it again. What does checkpoint 2 see now?
			CultureInfo.CurrentCulture = new CultureInfo("en-GB");
			Thread.CurrentPrincipal = new CustomPrincipal();
			_asyncLocalData.Value = "AsyncLocalData in Thread";

			// Step 2: checkpoint 2. The background thread, after assigning its own values
			RecordThreadValues(2);

			ExecutionContext.Run(mainThreadExecutionContext, _ =>
			{
				// Step 2: checkpoint 3. The same background thread, but running with the main thread's ExecutionContext
				RecordThreadValues(3);
			}, null);
		});

		// Execute Thread
		thread.Start();

		// Wait for Thread to Complete
		thread.Join();

		// Step 2: checkpoint 4. The main thread's values again
		RecordThreadValues(4);

		await Task.Run(() =>
		{
			// Step 2: checkpoint 5. Inside Task.Run()
			RecordThreadValues(5);
		});

		Task suppressedExecutionContextTask;
		using (ExecutionContext.SuppressFlow())
		{
			suppressedExecutionContextTask = Task.Run(() =>
			{
				// Step 2: checkpoint 6. Inside Task.Run() started while ExecutionContext flow is suppressed
				RecordThreadValues(6);
			});
		}

		// Step 2: the task is created inside the using block, but awaited only after the block ends
		await suppressedExecutionContextTask;
	}

	// Try it: the Try it button, which appears below the results on the Step 2 page once you run the experiment, runs this method. It is the end of RunAsync() with one change:
	// it awaits inside the using block instead of after it. Predict what happens before you click the button.
	public async Task AwaitInsideSuppressFlowAsync()
	{
		using (ExecutionContext.SuppressFlow())
		{
			// Step 2: the thread that starts the using block
			RecordThreadValues(1);

			await Task.Run(() => RecordThreadValues(2));

			// Step 2: the thread that runs the continuation, which is also the thread that ends the using block
			RecordThreadValues(3);
		}
	}

	void RecordThreadValues(int checkpoint) => log.Record(checkpoint, new ExecutionContextValues(
		CultureInfo.CurrentCulture.Name,
		Thread.CurrentPrincipal?.GetType().Name,
		_asyncLocalData.Value));
}