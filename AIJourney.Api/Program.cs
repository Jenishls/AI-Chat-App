using AIJourney.Api.Data;
using AIJourney.Api.Endpoints;
using AIJourney.Api.Options;
using AIJourney.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.AddCors(options =>
{
    options.AddPolicy("BlazorWeb", policy =>
    {
        policy.WithOrigins("http://localhost:5197", "https://localhost:7019")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddDbContext<AIJourneyDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("AIJourneyDb")));

builder.Services.Configure<OllamaOptions>(builder.Configuration.GetSection(OllamaOptions.SectionName));
builder.Services.AddSingleton<OllamaGenerationLimiter>();
builder.Services.AddHttpClient<OllamaChatClient>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IConfiguration>()
        .GetSection(OllamaOptions.SectionName)
        .Get<OllamaOptions>() ?? new OllamaOptions();

    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = Timeout.InfiniteTimeSpan;
});

builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AIJourneyDbContext>();
    await db.Database.MigrateAsync();
}
else
{
    app.UseHttpsRedirection();
}

app.UseCors("BlazorWeb");

app.MapGet("/health", () => Results.Ok(new
{
    Status = "Healthy"
})).WithName("Health");

app.MapAiEndpoints();
app.MapChatEndpoints();

app.Run();

public partial class Program;
