namespace InternalsLab;

public sealed record Observation<T>(int Checkpoint, int ThreadId, bool IsThreadPoolThread, T Value);