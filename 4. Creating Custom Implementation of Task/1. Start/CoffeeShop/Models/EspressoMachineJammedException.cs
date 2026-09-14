namespace CoffeeShop;

public sealed class EspressoMachineJammedException : Exception
{
	public EspressoMachineJammedException()
		: base("The espresso machine jammed while pulling a shot.")
	{
	}

	public EspressoMachineJammedException(string message)
		: base(message)
	{
	}

	public EspressoMachineJammedException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}