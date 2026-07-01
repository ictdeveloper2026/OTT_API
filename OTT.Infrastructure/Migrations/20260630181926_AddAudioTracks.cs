using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OTT.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAudioTracks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AudioTracks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Language = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    LanguageCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Label = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TrackIndex = table.Column<int>(type: "int", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AudioTracks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AudioTracks_VideoAssets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "VideoAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AudioTracks_AssetId",
                table: "AudioTracks",
                column: "AssetId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AudioTracks");
        }
    }
}
