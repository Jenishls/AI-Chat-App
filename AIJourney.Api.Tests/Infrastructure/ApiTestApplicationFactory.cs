using System.Net;
using System.Text;
using System.Text.Json;
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

    public int MaxConcurrentOllamaRequests { get; set; } = 1;

    public int OllamaQueueTimeoutSeconds { get; set; } = 1;

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
                QueueTimeoutSeconds = OllamaQueueTimeoutSeconds,
                MaxConcurrentRequests = MaxConcurrentOllamaRequests,
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
    private readonly object _gate = new();
    private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _responses = new();

    public List<HttpRequestMessage> Requests { get; } = [];

    public int ChatRequestCount
    {
        get
        {
            lock (_gate)
            {
                return Requests.Count(request => request.RequestUri?.AbsolutePath == "/api/chat");
            }
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _responses.Clear();
            Requests.Clear();
        }
    }

    public void QueueChatResponse(string content) =>
        EnqueueResponse((_, _) => Task.FromResult(JsonResponse(ChatResponseJson(content))));

    public void QueueChatResponseAfterDelay(string content, TimeSpan delay) =>
        EnqueueResponse(async (_, cancellationToken) =>
        {
            await Task.Delay(delay, cancellationToken);
            return JsonResponse(ChatResponseJson(content));
        });

    public void QueueStatusResponse(string version = "0.0-test") =>
        EnqueueResponse((_, _) => Task.FromResult(JsonResponse($$"""{"version":"{{version}}"}""")));

    public void QueueFailure(HttpStatusCode statusCode = HttpStatusCode.InternalServerError) =>
        EnqueueResponse((_, _) => Task.FromResult(new HttpResponseMessage(statusCode)));

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? queuedResponse = null;

        lock (_gate)
        {
            Requests.Add(request);

            if (_responses.Count > 0)
            {
                queuedResponse = _responses.Dequeue();
            }
        }

        if (queuedResponse is not null)
        {
            return await queuedResponse(request, cancellationToken);
        }

        return JsonResponse(ChatResponseJson("Test assistant response."));
    }

    private void EnqueueResponse(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response)
    {
        lock (_gate)
        {
            _responses.Enqueue(response);
        }
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private static string ChatResponseJson(string content) =>
        JsonSerializer.Serialize(new
        {
            message = new
            {
                role = "assistant",
                content
            },
            done = true
        });
}
