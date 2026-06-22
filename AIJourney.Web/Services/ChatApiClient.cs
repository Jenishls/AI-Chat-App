using System.Net.Http.Json;

namespace AIJourney.Web.Services;

public sealed class ChatApiClient(HttpClient httpClient)
{
    public async Task<List<ChatDto>> GetChatsAsync(CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync<List<ChatDto>>("api/chats", cancellationToken) ?? [];

    public async Task<ChatDto?> GetChatAsync(Guid chatId, CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync<ChatDto>($"api/chats/{chatId}", cancellationToken);

    public async Task<List<ChatMessageDto>> GetMessagesAsync(Guid chatId, CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync<List<ChatMessageDto>>($"api/chats/{chatId}/messages", cancellationToken) ?? [];

    public async Task<ChatDto> CreateChatAsync(string initialMessage, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            "api/chats",
            new CreateChatRequest(null, initialMessage),
            cancellationToken);

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ChatDto>(cancellationToken) ??
            throw new InvalidOperationException("The API returned an empty chat response.");
    }

    public async Task<List<ChatMessageDto>> CreateMessageAsync(
        Guid chatId,
        string content,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            $"api/chats/{chatId}/messages",
            new CreateMessageRequest(content),
            cancellationToken);

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<ChatMessageDto>>(cancellationToken) ?? [];
    }
}

public sealed record ChatDto(
    Guid Id,
    string Title,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string? Preview);

public sealed record ChatMessageDto(
    Guid Id,
    Guid ChatId,
    string Role,
    string Content,
    DateTimeOffset CreatedAtUtc);

public sealed record CreateChatRequest(string? Title, string? InitialMessage);

public sealed record CreateMessageRequest(string Content);
