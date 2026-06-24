using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OTT.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserSubscriptions_UserId",
                table: "UserSubscriptions");

            migrationBuilder.DropIndex(
                name: "IX_Contents_TenantId",
                table: "Contents");

            migrationBuilder.AlterColumn<string>(
                name: "GatewayPaymentId",
                table: "Payments",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WatchHistories_ProfileId_LastWatchedAt",
                table: "WatchHistories",
                columns: new[] { "ProfileId", "LastWatchedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_UserSubscriptions_UserId_Status_EndDate",
                table: "UserSubscriptions",
                columns: new[] { "UserId", "Status", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_GatewayPaymentId",
                table: "Payments",
                column: "GatewayPaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_TenantId_Status_CreatedAt",
                table: "Payments",
                columns: new[] { "TenantId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Contents_TenantId_IsTrending_ViewCount",
                table: "Contents",
                columns: new[] { "TenantId", "IsTrending", "ViewCount" });

            migrationBuilder.CreateIndex(
                name: "IX_Contents_TenantId_Status",
                table: "Contents",
                columns: new[] { "TenantId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WatchHistories_ProfileId_LastWatchedAt",
                table: "WatchHistories");

            migrationBuilder.DropIndex(
                name: "IX_UserSubscriptions_UserId_Status_EndDate",
                table: "UserSubscriptions");

            migrationBuilder.DropIndex(
                name: "IX_Payments_GatewayPaymentId",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_TenantId_Status_CreatedAt",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Contents_TenantId_IsTrending_ViewCount",
                table: "Contents");

            migrationBuilder.DropIndex(
                name: "IX_Contents_TenantId_Status",
                table: "Contents");

            migrationBuilder.AlterColumn<string>(
                name: "GatewayPaymentId",
                table: "Payments",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserSubscriptions_UserId",
                table: "UserSubscriptions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Contents_TenantId",
                table: "Contents",
                column: "TenantId");
        }
    }
}
