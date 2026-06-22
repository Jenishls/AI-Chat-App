using System.Net;
using System.Net.Http.Json;
using AIJourney.Api.Contracts;
using AIJourney.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AIJourney.Api.Tests;

public sealed class SecurityTests
{
    [Fact]
    public async Task Production_DoesNotExposeOpenApiDocument()
    {
        await using var factory = new ApiTestApplicationFactory()
            .WithWebHostBuilder(builder => builder.UseSetting("environment", "Production"));
        var client = factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Cors_DoesNotAllowUnknownOrigins()
    {
        await using var factory = new ApiTestApplicationFactory();
        factory.Ollama.Reset();
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/chats");
        request.Headers.Add("Origin", "https://evil.example");
        request.Headers.Add("Access-Control-Request-Method", "POST");

        var response = await client.SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Production_MutatingChatEndpointsRequireAuthentication()
    {
        await using var factory = new ApiTestApplicationFactory()
            .WithWebHostBuilder(builder => builder.UseSetting("environment", "Production"));
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/chats",
            new CreateChatRequest("Unauthorized", "This should not be accepted anonymously."));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
