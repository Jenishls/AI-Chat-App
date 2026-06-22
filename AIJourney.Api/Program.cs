using AIJourney.Api.Data;
using AIJourney.Api.Endpoints;
using AIJourney.Api.Models;
using AIJourney.Api.Options;
using AIJourney.Api.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var dataProtectionKeysPath = Path.Combine(builder.Environment.ContentRootPath, ".keys");
Directory.CreateDirectory(dataProtectionKeysPath);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));

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

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = true;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
    })
    .AddEntityFrameworkStores<AIJourneyDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "AIJourney.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.Events = new CookieAuthenticationEvents
    {
        OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        },
        OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }
    };
});
builder.Services.AddAuthorization();

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
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new
{
    Status = "Healthy"
})).WithName("Health");

app.MapAuthEndpoints();
app.MapAiEndpoints();
app.MapChatEndpoints();

app.Run();

public partial class Program;
