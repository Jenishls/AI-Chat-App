using System.Net;
using System.Net.Http.Json;
using AIJourney.Api.Contracts;
using AIJourney.Api.Data;
using AIJourney.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AIJourney.Api.Tests;

public sealed class AuthenticationOwnershipTests(ApiTestApplicationFactory factory)
    : IClassFixture<ApiTestApplicationFactory>
{
    [Fact]
    public async Task AnonymousUser_CannotAccessChatEndpoints()
    {
        factory.Ollama.Reset();
        var client = factory.CreateClient();

        var listResponse = await client.GetAsync("/api/chats");
        var createResponse = await client.PostAsJsonAsync(
            "/api/chats",
            new CreateChatRequest("Anonymous", "Nope"));

        Assert.Equal(HttpStatusCode.Unauthorized, listResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, createResponse.StatusCode);
    }

    [Fact]
    public async Task RegisteredUser_CanLoginAndCreateOwnedChat()
    {
        factory.Ollama.Reset();
        var client = factory.CreateClient();
        await client.RegisterAndLoginAsync("owner@example.test");

        var response = await client.PostAsJsonAsync(
            "/api/chats",
            new CreateChatRequest("Owned chat", null));

        response.EnsureSuccessStatusCode();
        var chat = await response.Content.ReadFromJsonAsync<ChatResponse>();

        Assert.NotNull(chat);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AIJourneyDbContext>();
        var storedChat = await db.Chats.FindAsync(chat.Id);

        Assert.NotNull(storedChat);
        Assert.False(string.IsNullOrWhiteSpace(storedChat.UserId));
    }

    [Fact]
    public async Task UsersCanOnlySeeTheirOwnChats()
    {
        factory.Ollama.Reset();
        var ownerClient = factory.CreateClient();
        await ownerClient.RegisterAndLoginAsync("owner-visible@example.test");
        var ownerChat = await ownerClient.CreateChatAsync("Owner chat");

        var otherClient = factory.CreateClient();
        await otherClient.RegisterAndLoginAsync("other-visible@example.test");
        var otherChat = await otherClient.CreateChatAsync("Other chat");

        var ownerChats = await ownerClient.GetFromJsonAsync<List<ChatResponse>>("/api/chats");
        var otherChats = await otherClient.GetFromJsonAsync<List<ChatResponse>>("/api/chats");

        Assert.Contains(ownerChats ?? [], chat => chat.Id == ownerChat.Id);
        Assert.DoesNotContain(ownerChats ?? [], chat => chat.Id == otherChat.Id);
        Assert.Contains(otherChats ?? [], chat => chat.Id == otherChat.Id);
        Assert.DoesNotContain(otherChats ?? [], chat => chat.Id == ownerChat.Id);
    }

    [Fact]
    public async Task OtherUserCannotReadOrMutateSomeoneElsesChat()
    {
        factory.Ollama.Reset();
        var ownerClient = factory.CreateClient();
        await ownerClient.RegisterAndLoginAsync("owner-private@example.test");
        var ownerChat = await ownerClient.CreateChatAsync("Owner private chat");

        var otherClient = factory.CreateClient();
        await otherClient.RegisterAndLoginAsync("other-private@example.test");

        var getChat = await otherClient.GetAsync($"/api/chats/{ownerChat.Id}");
        var getMessages = await otherClient.GetAsync($"/api/chats/{ownerChat.Id}/messages");
        var postMessage = await otherClient.PostAsJsonAsync(
            $"/api/chats/{ownerChat.Id}/messages",
            new CreateMessageRequest("I should not be able to write here."));
        var updateChat = await otherClient.PutAsJsonAsync(
            $"/api/chats/{ownerChat.Id}",
            new UpdateChatRequest("Stolen title"));
        var deleteChat = await otherClient.DeleteAsync($"/api/chats/{ownerChat.Id}");

        Assert.Equal(HttpStatusCode.NotFound, getChat.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, getMessages.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, postMessage.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, updateChat.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deleteChat.StatusCode);
    }

    private sealed record ChatResponse(
        Guid Id,
        string Title,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc,
        string? Preview);
}
