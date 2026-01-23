// See https://aka.ms/new-console-template for more information

using CreatingTaskFromScratch;

Console.WriteLine($"Starting Thread Id: {Environment.CurrentManagedThreadId}");

await CustomTask.Run(() => Console.WriteLine($"First CustomTask Id: {Environment.CurrentManagedThreadId}"));

await CustomTask.Delay(TimeSpan.FromSeconds(1));

Console.WriteLine($"Second CustomTask Id: {Environment.CurrentManagedThreadId}");

await CustomTask.Delay(TimeSpan.FromSeconds(1));

await CustomTask.Run(() => Console.WriteLine($"Third CustomTask Id: {Environment.CurrentManagedThreadId}"));