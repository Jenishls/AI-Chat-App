using System.Net;
using System.Text;
using System.Text.Json;
using AIJourney.Api.Options;
using AIJourney.Api.Services;
using Microsoft.Extensions.Options;
using OptionsFactory = Microsoft.Extensions.Options.Options;

namespace AIJourney.Api.Tests;

public sealed class OllamaChatClientTests
{
    [Fact]
    public async Task GetStatusAsync_ReturnsAvailable_WhenOllamaVersionEndpointResponds()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("http://localhost:11434/api/version", request.RequestUri?.ToString());

            return JsonResponse("""{"version":"0.6.0"}""");
        });

        var client = CreateClient(handler, new OllamaOptions { Model = "qwen2.5:3b" });

        var status = await client.GetStatusAsync();

        Assert.True(status.Available);
        Assert.Equal("qwen2.5:3b", status.Model);
        Assert.Equal("0.6.0", status.Version);
    }

    [Fact]
    public async Task GenerateAsync_SendsConfiguredModelAndNonStreamingChatRequest()
    {
        string? requestJson = null;
        var handler = new StubHttpMessageHandler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("http://localhost:11434/api/chat", request.RequestUri?.ToString());

            requestJson = await request.Content!.ReadAsStringAsync();
            return JsonResponse("""
                {
                  "message": {
                    "role": "assistant",
                    "content": "Hello from the configured model."
                  },
                  "done": true
                }
                """);
        });

        var client = CreateClient(
            handler,
            new OllamaOptions
            {
                Model = "qwen2.5:3b",
                Temperature = 0.2,
                RequestTimeoutSeconds = 5
            });

        var response = await client.GenerateAsync(
        [
            new OllamaMessage("user", "Hello")
        ]);

        Assert.Equal("Hello from the configured model.", response);
        Assert.NotNull(requestJson);

        using var document = JsonDocument.Parse(requestJson);
        var root = document.RootElement;

        Assert.Equal("qwen2.5:3b", root.GetProperty("model").GetString());
        Assert.False(root.GetProperty("stream").GetBoolean());
        Assert.Equal(0.2, root.GetProperty("options").GetProperty("temperature").GetDouble());
        Assert.Equal("user", root.GetProperty("messages")[0].GetProperty("role").GetString());
        Assert.Equal("Hello", root.GetProperty("messages")[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task GetStatusAsync_CanCheckLiveOllama_WhenRunLiveOllamaTestsIsEnabled()
    {
        if (!string.Equals(
            Environment.GetEnvironmentVariable("RUN_LIVE_OLLAMA_TESTS"),
            "true",
            StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var options = new OllamaOptions
        {
            BaseUrl = Environment.GetEnvironmentVariable("OLLAMA_BASE_URL") ?? "http://localhost:11434",
            Model = Environment.GetEnvironmentVariable("OLLAMA_MODEL") ?? "qwen2.5:3b",
            RequestTimeoutSeconds = 10
        };

        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(options.BaseUrl),
            Timeout = Timeout.InfiniteTimeSpan
        };

        var client = new OllamaChatClient(httpClient, OptionsFactory.Create(options));

        var status = await client.GetStatusAsync();

        Assert.True(status.Available, status.Message);
        Assert.Equal(options.Model, status.Model);
    }

    private static OllamaChatClient CreateClient(StubHttpMessageHandler handler, OllamaOptions options)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(options.BaseUrl),
            Timeout = Timeout.InfiniteTimeSpan
        };

        return new OllamaChatClient(httpClient, OptionsFactory.Create(options));
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
            : this(request => Task.FromResult(handler(request)))
        {
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            handler(request);
    }
}
