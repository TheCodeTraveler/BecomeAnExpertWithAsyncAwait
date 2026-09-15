namespace InternalsLab;

// Step 1: ThreadStatic
// RunAsync() plays the part of a console app's Main method: the Step 1 page starts it on a new dedicated thread, which acts as the main thread.
// Every log.Record(...) call is one checkpoint on the Step 1 page. Predict what each one records before you run it.
public sealed class ThreadStaticExperiment(CheckpointLog<int> log)
{
	// Step 1: [ThreadStatic] makes this static field thread-local
	// Try it: remove [ThreadStatic], then stop and run the app again. Restarting starts a fresh notebook, so try this one after the group review. Which checkpoints change, and which threads share the value now?
	// Try it: replace this field with static readonly AsyncLocal<int> _threadSpecificValue = new(), read and write its Value, and run the app again. Step 2 explains what changes.
	[ThreadStatic]
	static int _threadSpecificValue;

	public async Task RunAsync()
	{
		// Initializing thread-specific value for the main thread
		_threadSpecificValue = 100;

		// Step 1: checkpoint 1. The main thread, right after assigning 100
		log.Record(1, _threadSpecificValue);

		// Create two new threads. Thread 1 records checkpoints 2 and 3, and thread 2 records checkpoints 4 and 5.
		var thread1 = new Thread(() => ThreadMethod(2, 3));
		var thread2 = new Thread(() => ThreadMethod(4, 5));

		// Start the threads
		thread1.Start();
		thread2.Start();

		// Wait for threads to finish
		thread1.Join();
		thread2.Join();

		// Step 1: checkpoint 6. The main thread again, after the other threads have finished
		log.Record(6, _threadSpecificValue);

		// Step 1: Task.Yield() always schedules the rest of this method as a continuation.
		// This thread has no SynchronizationContext, so the continuation is queued to the thread pool.
		await Task.Yield();

		// Step 1: checkpoint 7. Whichever thread pool thread runs the continuation
		log.Record(7, _threadSpecificValue);
	}

	// Method to be run by each thread
	void ThreadMethod(int checkpointBeforeAssigning, int checkpointAfterAssigning)
	{
		// Step 1: checkpoints 2 and 4. This thread has not assigned anything yet
		log.Record(checkpointBeforeAssigning, _threadSpecificValue);

		// Initialize thread-specific value for this thread
		_threadSpecificValue = Random.Shared.Next(1, 100);

		// Step 1: checkpoints 3 and 5. This thread, right after assigning its own random value
		log.Record(checkpointAfterAssigning, _threadSpecificValue);
	}
}