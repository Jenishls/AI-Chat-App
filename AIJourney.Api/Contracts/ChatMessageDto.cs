namespace AIJourney.Api.Contracts;

public sealed record ChatMessageDto(
    Guid Id,
    Guid ChatId,
    string Role,
    string Content,
    DateTimeOffset CreatedAtUtc);
