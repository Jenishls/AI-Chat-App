using AIJourney.Api.Options;
using Microsoft.Extensions.Configuration;

namespace AIJourney.Api.Tests;

public sealed class OllamaOptionsTests
{
    [Fact]
    public void Configuration_BindsUpdatedOllamaModel()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ollama:BaseUrl"] = "http://localhost:11434",
                ["Ollama:Model"] = "qwen2.5:3b",
                ["Ollama:RequestTimeoutSeconds"] = "120",
                ["Ollama:QueueTimeoutSeconds"] = "10",
                ["Ollama:MaxConcurrentRequests"] = "1",
                ["Ollama:MaxHistoryMessages"] = "20",
                ["Ollama:Temperature"] = "0.7"
            })
            .Build();

        var options = configuration
            .GetSection(OllamaOptions.SectionName)
            .Get<OllamaOptions>();

        Assert.NotNull(options);
        Assert.Equal("qwen2.5:3b", options.Model);
        Assert.Equal("http://localhost:11434", options.BaseUrl);
        Assert.Equal(120, options.RequestTimeoutSeconds);
        Assert.Equal(10, options.QueueTimeoutSeconds);
        Assert.Equal(1, options.MaxConcurrentRequests);
        Assert.Equal(20, options.MaxHistoryMessages);
        Assert.Equal(0.7, options.Temperature);
    }
}
