namespace CoffeeShop;

public sealed class MachineJammedEventArgs(EspressoMachineJammedException exception) : EventArgs
{
	public EspressoMachineJammedException Exception { get; } = exception;
}