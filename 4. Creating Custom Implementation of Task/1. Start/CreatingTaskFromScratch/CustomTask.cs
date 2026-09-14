using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace CreatingTaskFromScratch;

sealed class CustomTask
{
	public bool IsCompleted
	{
		get
		{
			throw new NotImplementedException("CustomTask.IsCompleted is not implemented yet. Hint: return whether this CustomTask has completed. Another thread can complete it at any moment, so read the completion state under the same lock that SetResult() and SetException() use to write it.");
		}
	}

	public static CustomTask Delay(TimeSpan delay)
	{
		throw new NotImplementedException("CustomTask.Delay() is not implemented yet. Hint: return a CustomTask that a Timer completes once the delay has passed. Keep the Timer referenced so it cannot be garbage collected before it fires, and dispose it from inside its callback.");
	}

	public static CustomTask Run(Action action)
	{
		throw new NotImplementedException("CustomTask.Run() is not implemented yet. Hint: return a new CustomTask, queue the action to the thread pool, and complete the CustomTask with SetResult() when the action finishes or with SetException() when it throws.");
	}

	public void Wait()
	{
		throw new NotImplementedException("CustomTask.Wait() is not implemented yet. Hint: block the calling thread until this CustomTask completes, using ContinueWith() to signal a blocking wait primitive. Then rethrow the stored exception, if there is one, without losing its original stack trace.");
	}

	public CustomTask ContinueWith(Action action)
	{
		throw new NotImplementedException("CustomTask.ContinueWith() is not implemented yet. Hint: return a new CustomTask that completes when the action finishes or throws. If this CustomTask has already completed, queue the action to the thread pool now. Otherwise store the action, along with the caller's ExecutionContext, so SetResult() or SetException() can run it later. One CustomTask can have many continuations.");
	}

	public CustomTaskAwaiter GetAwaiter()
	{
		throw new NotImplementedException("CustomTask.GetAwaiter() is not implemented yet. Hint: return a CustomTaskAwaiter for this CustomTask. CustomTaskAwaiter.cs is already complete, so read it to see what it needs.");
	}

	public void SetResult()
	{
		throw new NotImplementedException("CustomTask.SetResult() is not implemented yet. Hint: mark this CustomTask as completed, then queue every continuation stored by ContinueWith() to the thread pool, running each one inside the ExecutionContext captured when it was stored. A CustomTask completes exactly once, so completing it a second time should throw an InvalidOperationException.");
	}

	public void SetException(Exception exception)
	{
		throw new NotImplementedException("CustomTask.SetException() is not implemented yet. Hint: store the exception so Wait() and await can rethrow it, then complete this CustomTask the same way SetResult() does. SetResult() and SetException() can share one private completion method.");
	}
}