namespace ImportPortal;

public sealed class Step1ScoreEveryRow : WorkshopStep
{
	// Roughly a second of validation on one thread, and a small fraction of that once every core helps
	const int _rowCount = 1_000;

	const string _missingRowsHint = "Some rows were never scored. Every row in orders has to get its RiskScore, exactly once, before RunImportAsync moves on to the next stage.";

	const string _oneCoreHint = "Validation kept about one core busy: one thread scored every row while the other cores sat idle. "
		+ "Nothing in the loop body depends on another row, so hand the rows to a Parallel loop and let every processor score them at the same time.";

	public override int Number => 1;

	public override string Scenario => "Score every row on every core";

	public override string Title => "RunImportAsync(), the validation stage";

	public override string Story => "Every night ImportPortal scores each uploaded row for risk before it does anything else. ScoreRisk() is pure CPU work, and no row depends on another, "
		+ "yet the import works through the file one row at a time on one thread while every other core on the server sits idle. "
		+ "The answer is right. It just takes exactly as long as it would on a machine with a single processor, and the files get bigger every month.";

	public override string SeeItInTheApp => "On the Import page beside this guide, press Run import. Validated reads 4,000 of 4,000, and its time is almost all of the Total card. "
		+ "Compare that with the processors available tile in the header: every one of them was available, and one did all the work. Write the Total down as your baseline.";

	public override string FileToChange => "Services/ImportService.cs";

	public override IReadOnlyList<string> Tasks { get; } =
	[
		"Replace the sequential validation foreach so every processor scores rows at the same time.",
		"Give the CPU stage its own ParallelOptions. Set MaxDegreeOfParallelism to a ceiling that suits CPU-bound work, and set CancellationToken to the token RunImportAsync already receives. Step 3 checks the token.",
		"Leave ScoreRisk(OrderRow) exactly as it is. The goal is to run it on more threads, not to make it cheaper.",
		"Do not add a shared counter, list, or dictionary that the loop body writes to. Each iteration only writes RiskScore on its own row.",
	];

	public override IReadOnlyList<string> Clues { get; } =
	[
		"The validation loop is a plain foreach, and nothing in its body depends on the row before it.",
		"Each iteration writes RiskScore on its own OrderRow and reads nothing another iteration wrote, so there is nothing to lock.",
		"The Parallel method that looks most like foreach is already in this file. One of its overloads takes ParallelOptions, and for CPU-bound work Environment.ProcessorCount is the right ceiling: more threads than cores only adds context switching.",
	];

	public override string TimeoutHint => "The import never finished. Check that the validation loop visits every row once and ends, and that nothing in RunImportAsync waits on work that cannot complete.";

	public override async Task Run(StepContext context)
	{
		var report = context.Report;

		// A fresh ImportService, so this check never touches the singletons the Import page uses
		var importService = new ImportService(new OrderFileService(), new CustomerApiService());

		report.Log($"Importing {_rowCount:N0} rows through a fresh ImportService. This machine has {Environment.ProcessorCount} processors");

		// CancellationToken.None, never context.Token: until Step 2 is fixed, the enrichment stage runs async void lambdas,
		// and a cancelled token reaching one of them throws where nothing can catch it, which ends the whole app
		var processorTimeBefore = GetProcessorTime();
		var importReport = await importService.RunImportAsync(_rowCount, CancellationToken.None).ConfigureAwait(false);
		var processorTime = GetProcessorTime() - processorTimeBefore;

		report.Log($"Validation scored {importReport.RowsValidated:N0} rows in {importReport.ValidateElapsed.TotalSeconds:F2}s");

		report.Expect("Every row is scored", _rowCount, importReport.RowsValidated, _missingRowsHint);

		// One thread can use at most one second of CPU time per second of validation, however hard it works.
		// Almost all of the CPU time an import uses is spent scoring rows, so CPU time divided by the validation time is how many cores were busy.
		// The other stages use a little CPU time too, which can push the result past the processor count, so it is capped there.
		var coresBusy = importReport.ValidateElapsed > TimeSpan.Zero ? Math.Min(processorTime / importReport.ValidateElapsed, Environment.ProcessorCount) : 0;

		report.Log($"The import used {processorTime.TotalSeconds:F2}s of CPU time, so validation kept about {coresBusy:F1} cores busy");

		if (Environment.ProcessorCount is 1)
		{
			report.Log("This machine reports 1 processor, so no loop can keep more than one core busy here. The next check passes on this machine without measuring anything");
			report.Expect("Validation keeps more than one core busy", "more than 1 core, on a machine with 2 or more processors", "not measured: this machine has 1 processor", true, _oneCoreHint);
			return;
		}

		// A single thread scores about 1.0. Every core helping scores close to the processor count, so these leave plenty of room for a busy laptop.
		var requiredCoresBusy = Environment.ProcessorCount >= 4 ? 2.0 : 1.5;

		report.Expect(
			"Validation keeps more than one core busy",
			$"at least {requiredCoresBusy:F1} cores",
			$"{coresBusy:F1} cores",
			coresBusy >= requiredCoresBusy,
			_oneCoreHint);
	}
}