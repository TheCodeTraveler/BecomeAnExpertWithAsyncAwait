namespace ImportPortal;

// Token is cancelled when the run is stopped, times out, or the app shuts down
public sealed record StepContext(StepReport Report, CancellationToken Token);