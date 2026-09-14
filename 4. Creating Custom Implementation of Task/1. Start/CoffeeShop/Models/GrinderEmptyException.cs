namespace CoffeeShop;

public sealed class GrinderEmptyException : InvalidOperationException
{
	public GrinderEmptyException()
		: base("The coffee grinder's hopper is empty.")
	{
	}

	public GrinderEmptyException(string message)
		: base(message)
	{
	}

	public GrinderEmptyException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}