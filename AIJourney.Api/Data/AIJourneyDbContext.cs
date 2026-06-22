using AIJourney.Api.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AIJourney.Api.Data;

public sealed class AIJourneyDbContext(DbContextOptions<AIJourneyDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Chat> Chats => Set<Chat>();

    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Chat>(entity =>
        {
            entity.HasKey(chat => chat.Id);
            entity.Property(chat => chat.UserId).HasMaxLength(450).IsRequired();
            entity.Property(chat => chat.Title).HasMaxLength(200).IsRequired();
            entity.Property(chat => chat.CreatedAtUtc).IsRequired();
            entity.Property(chat => chat.UpdatedAtUtc).IsRequired();
            entity.Property(chat => chat.IsDeleted).IsRequired();
            entity.Property(chat => chat.DeletedAtUtc);

            entity.HasIndex(chat => new { chat.UserId, chat.UpdatedAtUtc });
            entity.HasQueryFilter(chat => !chat.IsDeleted);

            entity.HasOne(chat => chat.User)
                .WithMany()
                .HasForeignKey(chat => chat.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(chat => chat.Messages)
                .WithOne(message => message.Chat)
                .HasForeignKey(message => message.ChatId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ChatMessage>(entity =>
        {
            entity.HasKey(message => message.Id);
            entity.Property(message => message.Role)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();
            entity.Property(message => message.Content).IsRequired();
            entity.Property(message => message.CreatedAtUtc).IsRequired();

            entity.HasIndex(message => new { message.ChatId, message.CreatedAtUtc });
            entity.HasQueryFilter(message => !message.Chat.IsDeleted);
        });
    }
}
