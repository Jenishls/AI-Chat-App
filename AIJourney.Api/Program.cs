using AIJourney.Api.Data;
using AIJourney.Api.Endpoints;
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

app.MapChatEndpoints();

app.Run();
