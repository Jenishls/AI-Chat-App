using AIJourney.Api.Contracts;
using AIJourney.Api.Data;
using AIJourney.Api.Models;
using AIJourney.Api.Options;
using AIJourney.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AIJourney.Api.Endpoints;

public static class ChatEndpoints
{
    private const string ModelBusyMessage = "The model is busy. Please try again.";
    private const string ModelTimeoutMessage = "The model took too long to respond. Please try again.";
    private const string ModelFailureMessage = "The model is unavailable right now. Please try again.";

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
        OllamaChatClient ollama,
        OllamaGenerationLimiter limiter,
        IOptions<OllamaOptions> ollamaOptions,
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

        ChatMessage? userMessage = null;
        ChatMessage? assistantMessage = null;

        db.Chats.Add(chat);

        if (!string.IsNullOrWhiteSpace(request.InitialMessage))
        {
            userMessage = CreateMessageEntity(chat.Id, ChatRole.User, request.InitialMessage.Trim(), now);
            db.ChatMessages.Add(userMessage);
        }

        await db.SaveChangesAsync(cancellationToken);

        if (userMessage is not null)
        {
            assistantMessage = await GenerateAndSaveAssistantMessageAsync(
                chat,
                db,
                ollama,
                limiter,
                ollamaOptions.Value,
                cancellationToken);
        }

        var preview = assistantMessage?.Content ?? userMessage?.Content;
        var dto = new ChatDto(chat.Id, chat.Title, chat.CreatedAtUtc, chat.UpdatedAtUtc, preview);
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
        OllamaChatClient ollama,
        OllamaGenerationLimiter limiter,
        IOptions<OllamaOptions> ollamaOptions,
        CancellationToken cancellationToken)
    {
        var content = request.Content.Trim();

        if (string.IsNullOrWhiteSpace(content))
        {
            return Results.BadRequest("Message content is required.");
        }

        var chat = await db.Chats.FirstOrDefaultAsync(chat => chat.Id == chatId, cancellationToken);

        if (chat is null)
        {
            return Results.NotFound();
        }

        var now = DateTimeOffset.UtcNow;
        var userMessage = CreateMessageEntity(chat.Id, ChatRole.User, content, now);

        db.ChatMessages.Add(userMessage);
        chat.UpdatedAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);

        var assistantMessage = await GenerateAndSaveAssistantMessageAsync(
            chat,
            db,
            ollama,
            limiter,
            ollamaOptions.Value,
            cancellationToken);

        var messages = new List<ChatMessageDto>
        {
            ToDto(userMessage),
            ToDto(assistantMessage)
        };

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

    private static ChatMessage CreateMessageEntity(Guid chatId, ChatRole role, string content, DateTimeOffset createdAtUtc)
    {
        return new ChatMessage
        {
            Id = Guid.NewGuid(),
            ChatId = chatId,
            Role = role,
            Content = content,
            CreatedAtUtc = createdAtUtc
        };
    }

    private static ChatMessageDto ToDto(ChatMessage message) =>
        new(message.Id, message.ChatId, message.Role.ToString(), message.Content, message.CreatedAtUtc);

    private static async Task<ChatMessage> GenerateAndSaveAssistantMessageAsync(
        Chat chat,
        AIJourneyDbContext db,
        OllamaChatClient ollama,
        OllamaGenerationLimiter limiter,
        OllamaOptions options,
        CancellationToken cancellationToken)
    {
        using var generationSlot = await limiter.TryAcquireAsync(cancellationToken);

        if (generationSlot is null)
        {
            return await AddAndSaveAssistantMessageAsync(chat, db, ModelBusyMessage, cancellationToken);
        }

        try
        {
            var history = await LoadOllamaHistoryAsync(db, chat.Id, options.MaxHistoryMessages, cancellationToken);
            var response = await ollama.GenerateAsync(history, cancellationToken);
            return await AddAndSaveAssistantMessageAsync(chat, db, response, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return await AddAndSaveAssistantMessageAsync(chat, db, ModelTimeoutMessage, cancellationToken);
        }
        catch
        {
            return await AddAndSaveAssistantMessageAsync(chat, db, ModelFailureMessage, cancellationToken);
        }
    }

    private static async Task<List<OllamaMessage>> LoadOllamaHistoryAsync(
        AIJourneyDbContext db,
        Guid chatId,
        int maxHistoryMessages,
        CancellationToken cancellationToken)
    {
        var take = Math.Max(1, maxHistoryMessages);

        var messages = await db.ChatMessages
            .AsNoTracking()
            .Where(message => message.ChatId == chatId)
            .OrderByDescending(message => message.CreatedAtUtc)
            .Take(take)
            .OrderBy(message => message.CreatedAtUtc)
            .Select(message => new OllamaMessage(
                message.Role == ChatRole.User ? "user" : "assistant",
                message.Content))
            .ToListAsync(cancellationToken);

        return messages;
    }

    private static async Task<ChatMessage> AddAndSaveAssistantMessageAsync(
        Chat chat,
        AIJourneyDbContext db,
        string content,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var assistantMessage = new ChatMessage
        {
            Id = Guid.NewGuid(),
            ChatId = chat.Id,
            Role = ChatRole.Assistant,
            Content = content,
            CreatedAtUtc = now
        };

        db.ChatMessages.Add(assistantMessage);
        chat.UpdatedAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);
        return assistantMessage;
    }

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
