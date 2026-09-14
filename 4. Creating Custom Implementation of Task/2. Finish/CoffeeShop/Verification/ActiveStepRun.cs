namespace CoffeeShop;

public sealed record ActiveStepRun(WorkshopStep Step, StepReport Report, EspressoMachine EspressoMachine, bool IsBaristaAutomatic);