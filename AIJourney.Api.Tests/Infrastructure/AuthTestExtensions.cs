using System.Net.Http.Json;
using AIJourney.Api.Contracts;

namespace AIJourney.Api.Tests.Infrastructure;

public static class AuthTestExtensions
{
    public const string DefaultPassword = "Password123!";

    public static async Task RegisterAndLoginAsync(this HttpClient client, string email)
    {
        var registerResponse = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest(email, DefaultPassword));
        registerResponse.EnsureSuccessStatusCode();

        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(email, DefaultPassword));
        loginResponse.EnsureSuccessStatusCode();
    }

    public static async Task<ChatResponse> CreateChatAsync(this HttpClient client, string title)
    {
        var response = await client.PostAsJsonAsync(
            "/api/chats",
            new CreateChatRequest(title, null));

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ChatResponse>() ??
            throw new InvalidOperationException("The API returned an empty chat response.");
    }

    public sealed record ChatResponse(
        Guid Id,
        string Title,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc,
        string? Preview);
}
