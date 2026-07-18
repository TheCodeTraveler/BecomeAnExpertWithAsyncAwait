using System.Text.Json.Serialization;

namespace HackerNews;

public record StoryModel([property: JsonPropertyName("by")] string Author, long Id, int Score, long Time, string Title, string Type, string Url)
{
	public DateTimeOffset CreatedAt { get; } = DateTimeOffset.FromUnixTimeSeconds(Time);

	public string Description => ToString();

	public override string ToString() => $"{Score} Points by {Author}, {GetAgeOfStory(CreatedAt)} ago";

	static string GetAgeOfStory(in DateTimeOffset storyCreatedAt)
	{
		var timespanSinceStoryCreated = DateTimeOffset.UtcNow - storyCreatedAt;

		return timespanSinceStoryCreated switch
		{
			_ when timespanSinceStoryCreated < TimeSpan.FromHours(1) => $"{Math.Ceiling(timespanSinceStoryCreated.TotalMinutes)} minutes",
			_ when timespanSinceStoryCreated >= TimeSpan.FromHours(1) && timespanSinceStoryCreated < TimeSpan.FromHours(2) => $"{Math.Floor(timespanSinceStoryCreated.TotalHours)} hour",
			_ when timespanSinceStoryCreated >= TimeSpan.FromHours(2) && timespanSinceStoryCreated < TimeSpan.FromHours(24) => $"{Math.Floor(timespanSinceStoryCreated.TotalHours)} hours",
			_ when timespanSinceStoryCreated >= TimeSpan.FromHours(24) && timespanSinceStoryCreated < TimeSpan.FromHours(48) => $"{Math.Floor(timespanSinceStoryCreated.TotalDays)} day",
			_ when timespanSinceStoryCreated >= TimeSpan.FromHours(48) => $"{Math.Floor(timespanSinceStoryCreated.TotalDays)} days",
			_ => string.Empty,
		};
	}
}