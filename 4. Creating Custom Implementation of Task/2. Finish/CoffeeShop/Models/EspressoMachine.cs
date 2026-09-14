namespace CoffeeShop;

// Stands in for the espresso machine's hardware SDK. Like many device SDKs, it has no Task anywhere:
// you start a shot, and the machine tells you it finished by raising an event on whichever thread noticed.
// In this app the barista presses the machine's buttons: FinishShot() and Jam() raise the events.
public sealed class EspressoMachine
{
	readonly Lock _lock = new();

	bool _isBrewing;
	string? _currentDrink;

	public event EventHandler? ShotPulled;

	public event EventHandler<MachineJammedEventArgs>? Jammed;

	public bool IsBrewing
	{
		get
		{
			lock (_lock)
			{
				return _isBrewing;
			}
		}
	}

	public string? CurrentDrink
	{
		get
		{
			lock (_lock)
			{
				return _currentDrink;
			}
		}
	}

	public void StartShot(string drink)
	{
		lock (_lock)
		{
			if (_isBrewing)
				throw new InvalidOperationException("The espresso machine is already pulling a shot.");

			_isBrewing = true;
			_currentDrink = drink;
		}
	}

	public void FinishShot()
	{
		if (StopBrewing())
			ShotPulled?.Invoke(this, EventArgs.Empty);
	}

	public void Jam()
	{
		if (StopBrewing())
			Jammed?.Invoke(this, new MachineJammedEventArgs(new EspressoMachineJammedException()));
	}

	// Returns false when no shot is brewing, so pressing a button twice raises only one event
	bool StopBrewing()
	{
		lock (_lock)
		{
			if (!_isBrewing)
				return false;

			_isBrewing = false;
			_currentDrink = null;

			return true;
		}
	}
}