using AIJourney.Api.Tests.Infrastructure;
using System.Net.Http.Json;

namespace AIJourney.Api.Tests;

public sealed class ApiHealthTests(ApiTestApplicationFactory factory)
    : IClassFixture<ApiTestApplicationFactory>
{
    [Fact]
    public async Task Health_ReturnsOk_WhenApiIsReady()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task AiStatus_ReturnsModelAvailability()
    {
        factory.Ollama.Reset();
        factory.Ollama.QueueStatusResponse("0.6.0");
        var client = factory.CreateClient();

        var status = await client.GetFromJsonAsync<OllamaStatusResponse>("/api/ai/status");

        Assert.NotNull(status);
        Assert.True(status.Available);
        Assert.Equal("test-model", status.Model);
        Assert.Equal("0.6.0", status.Version);
    }

    private sealed record OllamaStatusResponse(
        bool Available,
        string Model,
        string? Version,
        string Message);
}
