using System.Net;
using System.Net.Http.Json;
using System.Diagnostics;
using AIJourney.Api.Contracts;
using AIJourney.Api.Tests.Infrastructure;
using Xunit.Abstractions;

namespace AIJourney.Api.Tests;

public sealed class OllamaConcurrencyWorkflowTests(
    ApiTestApplicationFactory factory,
    ITestOutputHelper output)
    : IClassFixture<ApiTestApplicationFactory>
{
    [Fact]
    public async Task OneHundredConcurrentMessages_AreThrottledBeforeReachingOllama()
    {
        factory.Ollama.Reset();
        factory.Ollama.QueueChatResponseAfterDelay(
            "Only one request reached the model.",
            TimeSpan.FromMilliseconds(1500));

        var client = factory.CreateClient();
        await client.RegisterAndLoginAsync("throttled-concurrency@example.test");
        var chat = await CreateEmptyChatAsync(client);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var sendTasks = Enumerable.Range(1, 100)
            .Select(async index =>
            {
                await start.Task;

                var response = await client.PostAsJsonAsync(
                    $"/api/chats/{chat.Id}/messages",
                    new CreateMessageRequest($"Concurrent message {index}"));

                var messages = await response.Content.ReadFromJsonAsync<List<MessageResponse>>() ?? [];
                return new MessageSendResult(response.StatusCode, messages);
            })
            .ToList();

        start.SetResult();
        var results = await Task.WhenAll(sendTasks);
        var assistantMessages = results
            .SelectMany(result => result.Messages)
            .Where(message => message.Role == "Assistant")
            .Select(message => message.Content)
            .ToList();

        Assert.All(results, result => Assert.Equal(HttpStatusCode.Created, result.StatusCode));
        Assert.Equal(1, factory.Ollama.ChatRequestCount);
        Assert.Single(assistantMessages, "Only one request reached the model.");
        Assert.Equal(
            99,
            assistantMessages.Count(message => message == "The model is busy. Please try again."));

        var persistedMessages = await client.GetFromJsonAsync<List<MessageResponse>>($"/api/chats/{chat.Id}/messages") ?? [];
        var persistedUserMessages = persistedMessages
            .Where(message => message.Role == "User")
            .Select(message => message.Content)
            .ToList();
        var persistedAssistantMessages = persistedMessages
            .Where(message => message.Role == "Assistant")
            .Select(message => message.Content)
            .ToList();

        Assert.Equal(100, persistedUserMessages.Count(message => message.StartsWith("Concurrent message ")));
        Assert.Single(persistedAssistantMessages, "Only one request reached the model.");
        Assert.Equal(
            99,
            persistedAssistantMessages.Count(message => message == "The model is busy. Please try again."));
    }

    [Fact]
    public async Task OneHundredConcurrentMessages_WithHundredModelConnections_AllReachOllamaAndRecordsLatency()
    {
        await using var highConcurrencyFactory = new ApiTestApplicationFactory
        {
            MaxConcurrentOllamaRequests = 100,
            OllamaQueueTimeoutSeconds = 10
        };
        highConcurrencyFactory.Ollama.Reset();

        for (var index = 1; index <= 100; index++)
        {
            highConcurrencyFactory.Ollama.QueueChatResponseAfterDelay(
                $"Model response {index}",
                TimeSpan.FromMilliseconds(100));
        }

        var client = highConcurrencyFactory.CreateClient();
        await client.RegisterAndLoginAsync("high-concurrency@example.test");
        var chat = await CreateEmptyChatAsync(client);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var sendTasks = Enumerable.Range(1, 100)
            .Select(async index =>
            {
                await start.Task;

                var stopwatch = Stopwatch.StartNew();
                var response = await client.PostAsJsonAsync(
                    $"/api/chats/{chat.Id}/messages",
                    new CreateMessageRequest($"High concurrency message {index}"));
                stopwatch.Stop();

                var messages = await response.Content.ReadFromJsonAsync<List<MessageResponse>>() ?? [];
                return new TimedMessageSendResult(index, response.StatusCode, messages, stopwatch.Elapsed);
            })
            .ToList();

        var totalStopwatch = Stopwatch.StartNew();
        start.SetResult();
        var results = await Task.WhenAll(sendTasks);
        totalStopwatch.Stop();

        var responseTimes = results
            .Select(result => result.Elapsed)
            .OrderBy(duration => duration)
            .ToList();
        var p95 = responseTimes[(int)Math.Ceiling(responseTimes.Count * 0.95) - 1];
        var p99 = responseTimes[(int)Math.Ceiling(responseTimes.Count * 0.99) - 1];
        var averageMilliseconds = responseTimes.Average(duration => duration.TotalMilliseconds);

        output.WriteLine($"Total elapsed: {totalStopwatch.Elapsed.TotalMilliseconds:N0} ms");
        output.WriteLine($"Min response: {responseTimes.First().TotalMilliseconds:N0} ms");
        output.WriteLine($"Average response: {averageMilliseconds:N0} ms");
        output.WriteLine($"P95 response: {p95.TotalMilliseconds:N0} ms");
        output.WriteLine($"P99 response: {p99.TotalMilliseconds:N0} ms");
        output.WriteLine($"Max response: {responseTimes.Last().TotalMilliseconds:N0} ms");

        Assert.All(results, result => Assert.Equal(HttpStatusCode.Created, result.StatusCode));
        Assert.Equal(100, highConcurrencyFactory.Ollama.ChatRequestCount);
        Assert.DoesNotContain(
            results.SelectMany(result => result.Messages),
            message => message.Content == "The model is busy. Please try again.");
        Assert.Equal(
            100,
            results
                .SelectMany(result => result.Messages)
                .Count(message => message.Role == "Assistant" && message.Content.StartsWith("Model response ")));
    }

    private static async Task<ChatResponse> CreateEmptyChatAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/chats",
            new CreateChatRequest("Concurrency check", null));

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ChatResponse>() ??
            throw new InvalidOperationException("The API returned an empty chat response.");
    }

    private sealed record MessageSendResult(
        HttpStatusCode StatusCode,
        IReadOnlyList<MessageResponse> Messages);

    private sealed record TimedMessageSendResult(
        int RequestNumber,
        HttpStatusCode StatusCode,
        IReadOnlyList<MessageResponse> Messages,
        TimeSpan Elapsed);

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
