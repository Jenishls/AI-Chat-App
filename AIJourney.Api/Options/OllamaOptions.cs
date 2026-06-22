namespace AIJourney.Api.Options;

public sealed class OllamaOptions
{
    public const string SectionName = "Ollama";

    public string BaseUrl { get; set; } = "http://localhost:11434";

    public string Model { get; set; } = "qwen2.5:3b";

    public int RequestTimeoutSeconds { get; set; } = 120;

    public int QueueTimeoutSeconds { get; set; } = 10;

    public int MaxConcurrentRequests { get; set; } = 1;

    public int MaxHistoryMessages { get; set; } = 20;

    public double Temperature { get; set; } = 0.7;
}
