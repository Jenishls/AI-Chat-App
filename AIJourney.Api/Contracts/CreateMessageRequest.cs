namespace AIJourney.Api.Contracts;

public sealed record CreateMessageRequest(string Content, bool IncludeAssistantPlaceholder = true);
