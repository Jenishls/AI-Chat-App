namespace AIJourney.Api.Models;

public sealed class ChatMessage
{
    public Guid Id { get; set; }

    public Guid ChatId { get; set; }

    public Chat Chat { get; set; } = null!;

    public ChatRole Role { get; set; }

    public string Content { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }
}
