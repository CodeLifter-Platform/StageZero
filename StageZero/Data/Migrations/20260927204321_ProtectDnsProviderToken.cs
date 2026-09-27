using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StageZero.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProtectDnsProviderToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ApiToken",
                table: "DnsProviders",
                newName: "ProtectedApiToken");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ProtectedApiToken",
                table: "DnsProviders",
                newName: "ApiToken");
        }
    }
}
