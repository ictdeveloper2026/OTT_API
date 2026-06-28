using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OTT.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentContentId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ContentId",
                table: "Payments",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContentId",
                table: "Payments");
        }
    }
}
