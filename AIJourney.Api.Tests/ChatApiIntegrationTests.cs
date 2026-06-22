using System.Net;
using System.Net.Http.Json;
using AIJourney.Api.Contracts;
using AIJourney.Api.Tests.Infrastructure;

namespace AIJourney.Api.Tests;

public sealed class ChatApiIntegrationTests(ApiTestApplicationFactory factory)
    : IClassFixture<ApiTestApplicationFactory>
{
    [Fact]
    public async Task CreateChat_SavesInitialUserMessageAndAssistantReply()
    {
        factory.Ollama.Reset();
        factory.Ollama.QueueChatResponse("Assistant answer");
        var client = factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync(
            "/api/chats",
            new CreateChatRequest("  My chat  ", "  Hello model  "));

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var chat = await createResponse.Content.ReadFromJsonAsync<ChatResponse>();
        Assert.NotNull(chat);
        Assert.Equal("My chat", chat.Title);
        Assert.Equal("Assistant answer", chat.Preview);

        var messages = await client.GetFromJsonAsync<List<MessageResponse>>($"/api/chats/{chat.Id}/messages");

        Assert.NotNull(messages);
        Assert.Collection(
            messages,
            message =>
            {
                Assert.Equal("User", message.Role);
                Assert.Equal("Hello model", message.Content);
            },
            message =>
            {
                Assert.Equal("Assistant", message.Role);
                Assert.Equal("Assistant answer", message.Content);
            });
    }

    [Fact]
    public async Task CreateChat_NormalizesTitleFromInitialMessage_WhenTitleIsMissing()
    {
        factory.Ollama.Reset();
        var initialMessage = new string('a', 70);
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/chats",
            new CreateChatRequest(null, initialMessage));

        var chat = await response.Content.ReadFromJsonAsync<ChatResponse>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(chat);
        Assert.Equal($"{new string('a', 57)}...", chat.Title);
    }

    [Fact]
    public async Task UpdateChat_ReturnsBadRequest_WhenTitleIsBlank()
    {
        factory.Ollama.Reset();
        var client = factory.CreateClient();
        var chat = await CreateChatAsync(client);

        var response = await client.PutAsJsonAsync(
            $"/api/chats/{chat.Id}",
            new UpdateChatRequest("   "));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeleteChat_HidesChatAndMessagesFromPublicEndpoints()
    {
        factory.Ollama.Reset();
        var client = factory.CreateClient();
        var chat = await CreateChatAsync(client);

        var deleteResponse = await client.DeleteAsync($"/api/chats/{chat.Id}");
        var getChatResponse = await client.GetAsync($"/api/chats/{chat.Id}");
        var getMessagesResponse = await client.GetAsync($"/api/chats/{chat.Id}/messages");
        var chats = await client.GetFromJsonAsync<List<ChatResponse>>("/api/chats");

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, getChatResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, getMessagesResponse.StatusCode);
        Assert.DoesNotContain(chats ?? [], item => item.Id == chat.Id);
    }

    [Fact]
    public async Task CreateMessage_PersistsUserMessageAndFallback_WhenModelFails()
    {
        factory.Ollama.Reset();
        var client = factory.CreateClient();
        var chat = await CreateChatAsync(client);
        factory.Ollama.QueueFailure();

        var response = await client.PostAsJsonAsync(
            $"/api/chats/{chat.Id}/messages",
            new CreateMessageRequest("Will this work?"));

        var messages = await response.Content.ReadFromJsonAsync<List<MessageResponse>>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(messages);
        Assert.Collection(
            messages,
            message => Assert.Equal("Will this work?", message.Content),
            message => Assert.Equal("The model is unavailable right now. Please try again.", message.Content));
    }

    [Fact]
    public async Task CreateMessage_TrimsInputBeforePersisting()
    {
        factory.Ollama.Reset();
        var client = factory.CreateClient();
        var chat = await CreateChatAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/chats/{chat.Id}/messages",
            new CreateMessageRequest("   Trim this message   "));

        var messages = await response.Content.ReadFromJsonAsync<List<MessageResponse>>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(messages);
        Assert.Equal("Trim this message", messages[0].Content);
    }

    [Fact]
    public async Task CreateChat_TreatsSqlInjectionLikeInputAsLiteralText()
    {
        factory.Ollama.Reset();
        var client = factory.CreateClient();
        var hostileText = "'; DROP TABLE ChatMessages; --";

        var response = await client.PostAsJsonAsync(
            "/api/chats",
            new CreateChatRequest(hostileText, hostileText));

        response.EnsureSuccessStatusCode();
        var chat = await response.Content.ReadFromJsonAsync<ChatResponse>();
        var messages = await client.GetFromJsonAsync<List<MessageResponse>>($"/api/chats/{chat!.Id}/messages");
        var chatsResponse = await client.GetAsync("/api/chats");

        Assert.Equal(hostileText, chat.Title);
        Assert.Contains(messages ?? [], message => message.Content == hostileText);
        Assert.Equal(HttpStatusCode.OK, chatsResponse.StatusCode);
    }

    private static async Task<ChatResponse> CreateChatAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/chats",
            new CreateChatRequest("Test chat", "Hello"));

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ChatResponse>() ??
            throw new InvalidOperationException("The API returned an empty chat response.");
    }

    private sealed record ChatResponse(
        Guid Id,
        string Title,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc,
        string? Preview);

    private sealed record MessageResponse(
        Guid Id,
        Guid ChatId,
        string Role,
        string Content,
        DateTimeOffset CreatedAtUtc);
}
