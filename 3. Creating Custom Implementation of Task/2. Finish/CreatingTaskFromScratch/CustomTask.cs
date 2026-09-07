using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace CreatingTaskFromScratch;

sealed class CustomTask
{
	readonly Lock _lock = new();
	readonly List<(Action Continuation, ExecutionContext? Context)> _continuations = [];

	bool _completed;
	Exception? _exception;

	public bool IsCompleted
	{
		get
		{
			lock (_lock)
			{
				return _completed;
			}
		}
	}

	public static CustomTask Delay(TimeSpan delay)
	{
		CustomTask task = new();

		Timer? timer = null;
		timer = new Timer(_ =>
		{
			timer?.Dispose();
			task.SetResult();
		});

		timer.Change(delay, Timeout.InfiniteTimeSpan);

		return task;
	}

	public static CustomTask Run(Action action)
	{
		CustomTask task = new();

		ThreadPool.QueueUserWorkItem(_ =>
		{
			try
			{
				action();
				task.SetResult();
			}
			catch (Exception e)
			{
				task.SetException(e);
			}
		});

		return task;
	}

	public void Wait()
	{
		ManualResetEventSlim? resetEventSlim = null;

		lock (_lock)
		{
			if (!_completed)
			{
				resetEventSlim = new();
				ContinueWith(() => resetEventSlim.Set());
			}
		}

		resetEventSlim?.Wait();

		if (_exception is not null)
		{
			ExceptionDispatchInfo.Throw(_exception);
		}
	}

	public CustomTask ContinueWith(Action action)
	{
		CustomTask task = new();

		lock (_lock)
		{
			if (_completed)
			{
				ThreadPool.QueueUserWorkItem(_ => CompleteContinuationTask());
			}
			else
			{
				_continuations.Add((CompleteContinuationTask, ExecutionContext.Capture()));
			}
		}

		return task;

		void CompleteContinuationTask()
		{
			try
			{
				action();
				task.SetResult();
			}
			catch (Exception e)
			{
				task.SetException(e);
			}
		}
	}

	public CustomTaskAwaiter GetAwaiter() => new(this);

	public void SetResult() => CompleteTask(null);

	public void SetException(Exception exception) => CompleteTask(exception);

	void CompleteTask(Exception? exception)
	{
		List<(Action Continuation, ExecutionContext? Context)> continuationsToRun;

		lock (_lock)
		{
			if (_completed)
				throw new InvalidOperationException($"{nameof(CustomTask)} already completed. Cannot complete an already completed {nameof(CustomTask)}");

			_completed = true;
			_exception = exception;

			continuationsToRun = [.. _continuations];
			_continuations.Clear();
		}

		// Always queue (never inline) to avoid long ContinueWith chains recursively executing through CompleteTask.
		// UnsafeQueueUserWorkItem skips capturing this thread's context; the registrar's context is restored inside the work item.
		foreach (var pending in continuationsToRun)
		{
			ThreadPool.UnsafeQueueUserWorkItem(static state =>
			{
				var (continuation, context) = state;

				if (context is null)
				{
					continuation.Invoke();
				}
				else
				{
					ExecutionContext.Run(context, static s => ((Action?)s)?.Invoke(), continuation);
				}
			}, pending, preferLocal: false);
		}
	}
}