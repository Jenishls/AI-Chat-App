using AIJourney.Api.Services;

namespace AIJourney.Api.Endpoints;

public static class AiEndpoints
{
    public static RouteGroupBuilder MapAiEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/ai")
            .WithTags("AI");

        group.MapGet("/status", GetStatus).WithName("GetAiStatus");

        return group;
    }

    private static async Task<IResult> GetStatus(
        OllamaChatClient ollama,
        CancellationToken cancellationToken)
    {
        var status = await ollama.GetStatusAsync(cancellationToken);
        return Results.Ok(status);
    }
}
