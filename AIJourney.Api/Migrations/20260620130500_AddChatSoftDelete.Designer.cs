using System;
using System.Linq.Expressions;
using AIJourney.Api.Data;
using AIJourney.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIJourney.Api.Migrations;

[DbContext(typeof(AIJourneyDbContext))]
[Migration("20260620130500_AddChatSoftDelete")]
partial class AddChatSoftDelete
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
#pragma warning disable 612, 618
        modelBuilder
            .HasAnnotation("ProductVersion", "10.0.9")
            .HasAnnotation("Relational:MaxIdentifierLength", 128);

        modelBuilder.Entity<Chat>(b =>
            {
                b.Property<Guid>("Id")
                    .HasColumnType("uniqueidentifier");

                b.Property<DateTimeOffset>("CreatedAtUtc")
                    .HasColumnType("datetimeoffset");

                b.Property<DateTimeOffset?>("DeletedAtUtc")
                    .HasColumnType("datetimeoffset");

                b.Property<bool>("IsDeleted")
                    .HasColumnType("bit");

                b.Property<string>("Title")
                    .IsRequired()
                    .HasMaxLength(200)
                    .HasColumnType("nvarchar(200)");

                b.Property<DateTimeOffset>("UpdatedAtUtc")
                    .HasColumnType("datetimeoffset");

                b.HasKey("Id");

                b.ToTable("Chats");

                b.HasQueryFilter((Expression<Func<Chat, bool>>)(chat => !chat.IsDeleted));
            });

        modelBuilder.Entity<ChatMessage>(b =>
            {
                b.Property<Guid>("Id")
                    .HasColumnType("uniqueidentifier");

                b.Property<Guid>("ChatId")
                    .HasColumnType("uniqueidentifier");

                b.Property<string>("Content")
                    .IsRequired()
                    .HasColumnType("nvarchar(max)");

                b.Property<DateTimeOffset>("CreatedAtUtc")
                    .HasColumnType("datetimeoffset");

                b.Property<ChatRole>("Role")
                    .IsRequired()
                    .HasConversion<string>()
                    .HasMaxLength(20)
                    .HasColumnType("nvarchar(20)");

                b.HasKey("Id");

                b.HasIndex("ChatId", "CreatedAtUtc");

                b.ToTable("ChatMessages");

                b.HasQueryFilter((Expression<Func<ChatMessage, bool>>)(message => !message.Chat.IsDeleted));
            });

        modelBuilder.Entity("AIJourney.Api.Models.ChatMessage", b =>
            {
                b.HasOne("AIJourney.Api.Models.Chat", "Chat")
                    .WithMany("Messages")
                    .HasForeignKey("ChatId")
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired();

                b.Navigation("Chat");
            });

        modelBuilder.Entity("AIJourney.Api.Models.Chat", b =>
            {
                b.Navigation("Messages");
            });
#pragma warning restore 612, 618
    }
}
