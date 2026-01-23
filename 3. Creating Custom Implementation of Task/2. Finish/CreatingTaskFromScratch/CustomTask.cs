using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace CreatingTaskFromScratch;

sealed class CustomTask
{
	readonly Lock _lock = new();

	bool _completed;
	Action? _action;
	Exception? _exception;
	ExecutionContext? _context;

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

		new Timer(_ => task.SetResult()).Change(delay, Timeout.InfiniteTimeSpan);

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
			}
			else
			{
				_action = action;
				_context = ExecutionContext.Capture();
			}
		}

		return task;
	}

	public CustomTaskAwaiter GetAwaiter() => new(this);

	public void SetResult() => CompleteTask(null);

	public void SetException(Exception exception) => CompleteTask(exception);

	void CompleteTask(Exception? exception)
	{
		lock (_lock)
		{
			if (_completed)
				throw new InvalidOperationException(
					"DomeTrainTask already completed. Cannot set result of a completed DomeTrainTask");

			_completed = true;
			_exception = exception;

			if (_action is not null)
			{
				if (_context is null)
				{
					_action.Invoke();
				}
				else
				{
					ExecutionContext.Run(_context, state => ((Action?)state)?.Invoke(), _action);
				}
			}
		}
	}
}