namespace InternalsLab;

// Everything Step 2 reads from ExecutionContext at a checkpoint: CultureInfo.CurrentCulture.Name, Thread.CurrentPrincipal's type name, and the AsyncLocal<string> value
public sealed record ExecutionContextValues(string Culture, string? PrincipalType, string? AsyncLocalData);