using System.Net;
using System.Text;
using AIJourney.Api.Data;
using AIJourney.Api.Options;
using AIJourney.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace AIJourney.Api.Tests.Infrastructure;

public sealed class ApiTestApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"AIJourneyTests-{Guid.NewGuid()}";
    private readonly StubOllamaHandler _ollamaHandler = new();

    public StubOllamaHandler Ollama => _ollamaHandler;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            for (var index = services.Count - 1; index >= 0; index--)
            {
                var serviceType = services[index].ServiceType;
                if (serviceType == typeof(DbContextOptions<AIJourneyDbContext>) ||
                    serviceType.FullName?.Contains("IDbContextOptionsConfiguration") == true)
                {
                    services.RemoveAt(index);
                }
            }

            services.RemoveAll<OllamaChatClient>();

            services.AddDbContext<AIJourneyDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName));

            var ollamaOptions = new OllamaOptions
            {
                BaseUrl = "http://ollama.test",
                Model = "test-model",
                RequestTimeoutSeconds = 5,
                QueueTimeoutSeconds = 1,
                MaxConcurrentRequests = 1,
                MaxHistoryMessages = 3,
                Temperature = 0.2
            };

            services.AddSingleton<IOptions<OllamaOptions>>(
                Microsoft.Extensions.Options.Options.Create(ollamaOptions));
            services.AddSingleton(_ =>
            {
                var httpClient = new HttpClient(_ollamaHandler)
                {
                    BaseAddress = new Uri(ollamaOptions.BaseUrl),
                    Timeout = Timeout.InfiniteTimeSpan
                };

                return new OllamaChatClient(
                    httpClient,
                    Microsoft.Extensions.Options.Options.Create(ollamaOptions));
            });

            using var scope = services.BuildServiceProvider().CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AIJourneyDbContext>();
            db.Database.EnsureCreated();
        });
    }
}

public sealed class StubOllamaHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

    public List<HttpRequestMessage> Requests { get; } = [];

    public void Reset()
    {
        _responses.Clear();
        Requests.Clear();
    }

    public void QueueChatResponse(string content) =>
        _responses.Enqueue(_ => JsonResponse($$"""
            {
              "message": {
                "role": "assistant",
                "content": "{{content}}"
              },
              "done": true
            }
            """));

    public void QueueStatusResponse(string version = "0.0-test") =>
        _responses.Enqueue(_ => JsonResponse($$"""{"version":"{{version}}"}"""));

    public void QueueFailure(HttpStatusCode statusCode = HttpStatusCode.InternalServerError) =>
        _responses.Enqueue(_ => new HttpResponseMessage(statusCode));

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);

        if (_responses.Count == 0)
        {
            return Task.FromResult(JsonResponse("""
                {
                  "message": {
                    "role": "assistant",
                    "content": "Test assistant response."
                  },
                  "done": true
                }
                """));
        }

        return Task.FromResult(_responses.Dequeue()(request));
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
}
