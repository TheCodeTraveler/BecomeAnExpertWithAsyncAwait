using CreatingTaskFromScratch;

Console.WriteLine($"Starting Thread Id: {Environment.CurrentManagedThreadId}");

await CustomTask.Run(() => Console.WriteLine($"First {nameof(CustomTask)} Id: {Environment.CurrentManagedThreadId}"));

await CustomTask.Delay(TimeSpan.FromSeconds(1));

Console.WriteLine($"Second {nameof(CustomTask)} Id: {Environment.CurrentManagedThreadId}");

await CustomTask.Delay(TimeSpan.FromSeconds(1));

await CustomTask.Run(() => Console.WriteLine($"Third {nameof(CustomTask)} Id: {Environment.CurrentManagedThreadId}"));