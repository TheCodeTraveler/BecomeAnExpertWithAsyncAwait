using System.Runtime.CompilerServices;

namespace CoffeeShop;

public sealed class CoffeeGrinder(int gramsInHopper)
{
	readonly Lock _lock = new();

	int _gramsInHopper = gramsInHopper;

	// Keep this frame visible so the stack trace shows where the grinder failed
	[MethodImpl(MethodImplOptions.NoInlining)]
	public void Grind(Order order)
	{
		lock (_lock)
		{
			if (_gramsInHopper < order.GramsOfCoffee)
				throw new GrinderEmptyException();

			_gramsInHopper -= order.GramsOfCoffee;
		}
	}
}