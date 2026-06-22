using AIJourney.Api.Data;
using AIJourney.Api.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AIJourney.Api.Tests;

public sealed class DatabaseBehaviorTests
{
    [Fact]
    public async Task SoftDeletedChatsAndMessagesAreHiddenByDefaultQueryFilters()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AIJourneyDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var setupDb = new AIJourneyDbContext(options))
        {
            await setupDb.Database.EnsureCreatedAsync();
            var user = CreateUser();
            setupDb.Users.Add(user);

            var deletedChat = new Chat
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Title = "Deleted",
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                IsDeleted = true,
                DeletedAtUtc = DateTimeOffset.UtcNow,
                Messages =
                [
                    new ChatMessage
                    {
                        Id = Guid.NewGuid(),
                        Role = ChatRole.User,
                        Content = "Hidden",
                        CreatedAtUtc = DateTimeOffset.UtcNow
                    }
                ]
            };

            setupDb.Chats.Add(deletedChat);
            await setupDb.SaveChangesAsync();
        }

        await using var db = new AIJourneyDbContext(options);

        Assert.Empty(await db.Chats.ToListAsync());
        Assert.Empty(await db.ChatMessages.ToListAsync());
        Assert.Single(await db.Chats.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task SchemaCanBeCreatedFromModel()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AIJourneyDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new AIJourneyDbContext(options);

        await db.Database.EnsureCreatedAsync();

        Assert.True(await db.Database.CanConnectAsync());
    }

    [Fact]
    public async Task HardDeletingChatCascadesMessages()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AIJourneyDbContext>()
            .UseSqlite(connection)
            .Options;

        var chatId = Guid.NewGuid();

        await using (var setupDb = new AIJourneyDbContext(options))
        {
            await setupDb.Database.EnsureCreatedAsync();
            var user = CreateUser();
            setupDb.Users.Add(user);
            setupDb.Chats.Add(new Chat
            {
                Id = chatId,
                UserId = user.Id,
                Title = "Cascade test",
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                Messages =
                [
                    new ChatMessage
                    {
                        Id = Guid.NewGuid(),
                        Role = ChatRole.User,
                        Content = "Delete me with my chat",
                        CreatedAtUtc = DateTimeOffset.UtcNow
                    }
                ]
            });
            await setupDb.SaveChangesAsync();
        }

        await using (var deleteDb = new AIJourneyDbContext(options))
        {
            var chat = await deleteDb.Chats
                .Include(item => item.Messages)
                .SingleAsync(item => item.Id == chatId);

            deleteDb.Chats.Remove(chat);
            await deleteDb.SaveChangesAsync();
        }

        await using var verifyDb = new AIJourneyDbContext(options);

        Assert.Empty(await verifyDb.Chats.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await verifyDb.ChatMessages.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task ChatRoleIsPersistedAsString()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AIJourneyDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var setupDb = new AIJourneyDbContext(options))
        {
            await setupDb.Database.EnsureCreatedAsync();
            var user = CreateUser();
            setupDb.Users.Add(user);
            setupDb.Chats.Add(new Chat
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Title = "Role conversion",
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                Messages =
                [
                    new ChatMessage
                    {
                        Id = Guid.NewGuid(),
                        Role = ChatRole.Assistant,
                        Content = "Stored role",
                        CreatedAtUtc = DateTimeOffset.UtcNow
                    }
                ]
            });
            await setupDb.SaveChangesAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Role FROM ChatMessages LIMIT 1";
        var storedRole = await command.ExecuteScalarAsync();

        Assert.Equal("Assistant", storedRole);
    }

    [Fact]
    public void ModelDefinesRequiredFieldsAndLengthLimits()
    {
        var options = new DbContextOptionsBuilder<AIJourneyDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        using var db = new AIJourneyDbContext(options);
        var model = db.Model;
        var chat = model.FindEntityType(typeof(Chat))!;
        var message = model.FindEntityType(typeof(ChatMessage))!;

        Assert.False(chat.FindProperty(nameof(Chat.Title))!.IsNullable);
        Assert.False(chat.FindProperty(nameof(Chat.UserId))!.IsNullable);
        Assert.Equal(450, chat.FindProperty(nameof(Chat.UserId))!.GetMaxLength());
        Assert.Equal(200, chat.FindProperty(nameof(Chat.Title))!.GetMaxLength());
        Assert.False(message.FindProperty(nameof(ChatMessage.Content))!.IsNullable);
        Assert.False(message.FindProperty(nameof(ChatMessage.Role))!.IsNullable);
        Assert.Equal(20, message.FindProperty(nameof(ChatMessage.Role))!.GetMaxLength());
    }

    private static ApplicationUser CreateUser()
    {
        var id = Guid.NewGuid().ToString();
        return new ApplicationUser
        {
            Id = id,
            UserName = $"{id}@example.test",
            NormalizedUserName = $"{id}@EXAMPLE.TEST",
            Email = $"{id}@example.test",
            NormalizedEmail = $"{id}@EXAMPLE.TEST"
        };
    }
}
