using AIJourney.Api.Contracts;
using AIJourney.Api.Data;
using AIJourney.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace AIJourney.Api.Endpoints;

public static class ChatEndpoints
{
    public static RouteGroupBuilder MapChatEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/chats")
            .WithTags("Chats");

        group.MapGet("/", GetChats).WithName("GetChats");
        group.MapGet("/{chatId:guid}", GetChat).WithName("GetChat");
        group.MapPost("/", CreateChat).WithName("CreateChat");
        group.MapPut("/{chatId:guid}", UpdateChat).WithName("UpdateChat");
        group.MapDelete("/{chatId:guid}", DeleteChat).WithName("DeleteChat");
        group.MapGet("/{chatId:guid}/messages", GetMessages).WithName("GetChatMessages");
        group.MapPost("/{chatId:guid}/messages", CreateMessage).WithName("CreateChatMessage");
        group.MapDelete("/{chatId:guid}/messages/{messageId:guid}", DeleteMessage).WithName("DeleteChatMessage");

        return group;
    }

    private static async Task<IResult> GetChats(AIJourneyDbContext db, CancellationToken cancellationToken)
    {
        var chats = await db.Chats
            .AsNoTracking()
            .OrderByDescending(chat => chat.UpdatedAtUtc)
            .Select(chat => new ChatDto(
                chat.Id,
                chat.Title,
                chat.CreatedAtUtc,
                chat.UpdatedAtUtc,
                chat.Messages
                    .OrderByDescending(message => message.CreatedAtUtc)
                    .Select(message => message.Content)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);

        return Results.Ok(chats);
    }

    private static async Task<IResult> GetChat(Guid chatId, AIJourneyDbContext db, CancellationToken cancellationToken)
    {
        var chat = await db.Chats
            .AsNoTracking()
            .Where(chat => chat.Id == chatId)
            .Select(chat => new ChatDto(
                chat.Id,
                chat.Title,
                chat.CreatedAtUtc,
                chat.UpdatedAtUtc,
                chat.Messages
                    .OrderByDescending(message => message.CreatedAtUtc)
                    .Select(message => message.Content)
                    .FirstOrDefault()))
            .FirstOrDefaultAsync(cancellationToken);

        return chat is null ? Results.NotFound() : Results.Ok(chat);
    }

    private static async Task<IResult> CreateChat(
        CreateChatRequest request,
        AIJourneyDbContext db,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var title = NormalizeTitle(request.Title, request.InitialMessage);

        var chat = new Chat
        {
            Id = Guid.NewGuid(),
            Title = title,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            IsDeleted = false
        };

        db.Chats.Add(chat);

        if (!string.IsNullOrWhiteSpace(request.InitialMessage))
        {
            AddMessage(chat, ChatRole.User, request.InitialMessage.Trim(), now);
            AddMessage(chat, ChatRole.Assistant, "This is where the model response will appear once the AI service is connected.", now.AddMilliseconds(1));
        }

        await db.SaveChangesAsync(cancellationToken);

        var dto = new ChatDto(chat.Id, chat.Title, chat.CreatedAtUtc, chat.UpdatedAtUtc, request.InitialMessage?.Trim());
        return Results.Created($"/api/chats/{chat.Id}", dto);
    }

    private static async Task<IResult> UpdateChat(
        Guid chatId,
        UpdateChatRequest request,
        AIJourneyDbContext db,
        CancellationToken cancellationToken)
    {
        var title = request.Title.Trim();

        if (string.IsNullOrWhiteSpace(title))
        {
            return Results.BadRequest("Chat title is required.");
        }

        var chat = await db.Chats.FirstOrDefaultAsync(chat => chat.Id == chatId, cancellationToken);

        if (chat is null)
        {
            return Results.NotFound();
        }

        chat.Title = title;
        chat.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(new ChatDto(chat.Id, chat.Title, chat.CreatedAtUtc, chat.UpdatedAtUtc, null));
    }

    private static async Task<IResult> DeleteChat(Guid chatId, AIJourneyDbContext db, CancellationToken cancellationToken)
    {
        var chat = await db.Chats.FirstOrDefaultAsync(chat => chat.Id == chatId, cancellationToken);

        if (chat is null)
        {
            return Results.NotFound();
        }

        var now = DateTimeOffset.UtcNow;
        chat.IsDeleted = true;
        chat.DeletedAtUtc = now;
        chat.UpdatedAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> GetMessages(Guid chatId, AIJourneyDbContext db, CancellationToken cancellationToken)
    {
        var chatExists = await db.Chats.AnyAsync(chat => chat.Id == chatId, cancellationToken);

        if (!chatExists)
        {
            return Results.NotFound();
        }

        var messages = await db.ChatMessages
            .AsNoTracking()
            .Where(message => message.ChatId == chatId)
            .OrderBy(message => message.CreatedAtUtc)
            .Select(message => new ChatMessageDto(
                message.Id,
                message.ChatId,
                message.Role.ToString(),
                message.Content,
                message.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return Results.Ok(messages);
    }

    private static async Task<IResult> CreateMessage(
        Guid chatId,
        CreateMessageRequest request,
        AIJourneyDbContext db,
        CancellationToken cancellationToken)
    {
        var content = request.Content.Trim();

        if (string.IsNullOrWhiteSpace(content))
        {
            return Results.BadRequest("Message content is required.");
        }

        var chat = await db.Chats
            .Include(chat => chat.Messages)
            .FirstOrDefaultAsync(chat => chat.Id == chatId, cancellationToken);

        if (chat is null)
        {
            return Results.NotFound();
        }

        var now = DateTimeOffset.UtcNow;
        var userMessage = AddMessage(chat, ChatRole.User, content, now);
        ChatMessage? assistantMessage = null;

        if (request.IncludeAssistantPlaceholder)
        {
            assistantMessage = AddMessage(
                chat,
                ChatRole.Assistant,
                "This is where the model response will appear once the AI service is connected.",
                now.AddMilliseconds(1));
        }

        chat.UpdatedAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);

        var messages = new List<ChatMessageDto>
        {
            ToDto(userMessage)
        };

        if (assistantMessage is not null)
        {
            messages.Add(ToDto(assistantMessage));
        }

        return Results.Created($"/api/chats/{chatId}/messages/{userMessage.Id}", messages);
    }

    private static async Task<IResult> DeleteMessage(
        Guid chatId,
        Guid messageId,
        AIJourneyDbContext db,
        CancellationToken cancellationToken)
    {
        var chat = await db.Chats.FirstOrDefaultAsync(chat => chat.Id == chatId, cancellationToken);

        if (chat is null)
        {
            return Results.NotFound();
        }

        var rows = await db.ChatMessages
            .Where(message => message.ChatId == chatId && message.Id == messageId)
            .ExecuteDeleteAsync(cancellationToken);

        if (rows == 0)
        {
            return Results.NotFound();
        }

        chat.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    private static ChatMessage AddMessage(Chat chat, ChatRole role, string content, DateTimeOffset createdAtUtc)
    {
        var message = new ChatMessage
        {
            Id = Guid.NewGuid(),
            ChatId = chat.Id,
            Role = role,
            Content = content,
            CreatedAtUtc = createdAtUtc
        };

        chat.Messages.Add(message);
        return message;
    }

    private static ChatMessageDto ToDto(ChatMessage message) =>
        new(message.Id, message.ChatId, message.Role.ToString(), message.Content, message.CreatedAtUtc);

    private static string NormalizeTitle(string? title, string? initialMessage)
    {
        var candidate = !string.IsNullOrWhiteSpace(title)
            ? title.Trim()
            : initialMessage?.Trim();

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return "New chat";
        }

        return candidate.Length <= 60 ? candidate : $"{candidate[..57]}...";
    }
}
