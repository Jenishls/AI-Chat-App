using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AIJourney.Api.Options;
using Microsoft.Extensions.Options;

namespace AIJourney.Api.Services;

public sealed class OllamaChatClient(HttpClient httpClient, IOptions<OllamaOptions> options)
{
    private readonly OllamaOptions _options = options.Value;

    public async Task<OllamaStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var version = await httpClient.GetFromJsonAsync<OllamaVersionResponse>("api/version", cancellationToken);
            return new OllamaStatus(true, _options.Model, version?.Version, "Ollama is ready.");
        }
        catch
        {
            return new OllamaStatus(false, _options.Model, null, "Ollama is not running or is unreachable.");
        }
    }

    public async Task<string> GenerateAsync(
        IReadOnlyList<OllamaMessage> messages,
        CancellationToken cancellationToken = default)
    {
        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(Math.Max(1, _options.RequestTimeoutSeconds)));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        var request = new OllamaChatRequest(
            _options.Model,
            messages,
            Stream: false,
            Options: new OllamaRequestOptions(_options.Temperature));

        var response = await httpClient.PostAsJsonAsync("api/chat", request, linked.Token);
        response.EnsureSuccessStatusCode();

        var chatResponse = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(linked.Token);
        var content = chatResponse?.Message?.Content?.Trim();

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("Ollama returned an empty response.");
        }

        return content;
    }
}

public sealed record OllamaStatus(bool Available, string Model, string? Version, string Message);

public sealed record OllamaMessage(string Role, string Content);

public sealed record OllamaChatRequest(
    string Model,
    IReadOnlyList<OllamaMessage> Messages,
    bool Stream,
    OllamaRequestOptions Options);

public sealed record OllamaRequestOptions(
    [property: JsonPropertyName("temperature")] double Temperature);

public sealed record OllamaChatResponse(
    [property: JsonPropertyName("message")] OllamaResponseMessage? Message,
    [property: JsonPropertyName("done")] bool Done);

public sealed record OllamaResponseMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content);

public sealed record OllamaVersionResponse(
    [property: JsonPropertyName("version")] string Version);
