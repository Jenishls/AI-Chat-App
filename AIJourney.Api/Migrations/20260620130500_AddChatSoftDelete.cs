using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIJourney.Api.Migrations;

public partial class AddChatSoftDelete : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "DeletedAtUtc",
            table: "Chats",
            type: "datetimeoffset",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "IsDeleted",
            table: "Chats",
            type: "bit",
            nullable: false,
            defaultValue: false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "DeletedAtUtc",
            table: "Chats");

        migrationBuilder.DropColumn(
            name: "IsDeleted",
            table: "Chats");
    }
}
