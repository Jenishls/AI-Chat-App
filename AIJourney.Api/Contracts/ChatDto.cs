namespace AIJourney.Api.Contracts;

public sealed record ChatDto(
    Guid Id,
    string Title,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string? Preview);
