using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using AIJourney.Web.Services;

namespace AIJourney.Web.Tests;

public sealed class UxUiSmokeTests
{
    [Fact]
    public async Task HomePage_RendersCoreChatExperience()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("AI Journey Chat", html);
        Assert.Contains("New chat", html);
        Assert.Contains("Lets start our conversation", html);
        Assert.Contains("Explain a concept", html);
        Assert.Contains("Help me build a project", html);
        Assert.Contains("Plan my AI learning Path", html);
    }

    [Fact]
    public async Task UnknownPage_RendersNotFoundExperience()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = true
        });

        var response = await client.GetAsync("/missing-page");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("not found", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConversationPage_EncodesDangerousMessageContent()
    {
        var chatId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var dangerousContent = "<script>alert('xss')</script><img src=x onerror=alert(1)>";
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.AddHttpClient<ChatApiClient>()
                        .ConfigurePrimaryHttpMessageHandler(() =>
                            new StubChatApiHandler(chatId, dangerousContent));
                });
            });
        var client = factory.CreateClient();

        var html = await client.GetStringAsync($"/chats/{chatId}");

        Assert.DoesNotContain(dangerousContent, html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("&lt;img", html);
    }

    private sealed class StubChatApiHandler(Guid chatId, string dangerousContent) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath.TrimEnd('/');
            HttpResponseMessage response;

            if (request.Method == HttpMethod.Get && path == "/api/chats")
            {
                response = JsonResponse(new List<ChatDto>
                {
                    new ChatDto(chatId, "XSS check", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, dangerousContent)
                });
            }
            else if (request.Method == HttpMethod.Get && path == $"/api/chats/{chatId}")
            {
                response = JsonResponse(
                    new ChatDto(chatId, "XSS check", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, dangerousContent));
            }
            else if (request.Method == HttpMethod.Get && path == $"/api/chats/{chatId}/messages")
            {
                response = JsonResponse(new List<ChatMessageDto>
                {
                    new ChatMessageDto(Guid.NewGuid(), chatId, "User", dangerousContent, DateTimeOffset.UtcNow)
                });
            }
            else
            {
                response = new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            return Task.FromResult(response);
        }

        private static HttpResponseMessage JsonResponse<T>(T value) =>
            new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(value)
            };
    }
}
