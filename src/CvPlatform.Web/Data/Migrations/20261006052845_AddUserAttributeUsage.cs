using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CvPlatform.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserAttributeUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserAttributeUsages",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    AttributeDefinitionId = table.Column<int>(type: "integer", nullable: false),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAttributeUsages", x => new { x.UserId, x.AttributeDefinitionId });
                    table.ForeignKey(
                        name: "FK_UserAttributeUsages_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserAttributeUsages_AttributeDefinitions_AttributeDefinitio~",
                        column: x => x.AttributeDefinitionId,
                        principalTable: "AttributeDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserAttributeUsages_AttributeDefinitionId",
                table: "UserAttributeUsages",
                column: "AttributeDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_UserAttributeUsages_UserId_LastUsedAt",
                table: "UserAttributeUsages",
                columns: new[] { "UserId", "LastUsedAt" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserAttributeUsages");
        }
    }
}
