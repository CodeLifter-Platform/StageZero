using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StageZero.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class SingleCloudflareCredential : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CloudflareCredentials",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProtectedApiToken = table.Column<string>(type: "text", nullable: false),
                    AccountId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AccountName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    TokenId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ExpiresOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CloudflareCredentials", x => x.Id);
                });

            // One token for everything: carry over the tunnel's (its permissions are the
            // superset), else the first DNS provider's, enabled ones first. Both are encrypted
            // under the same Data Protection purpose, so the ciphertext moves as-is; a provider
            // token from before encryption is encrypted at startup (CloudflareTokenStore).
            migrationBuilder.Sql("""
                INSERT INTO "CloudflareCredentials" ("ProtectedApiToken", "AccountId", "UpdatedAt")
                SELECT "ProtectedApiToken", NULLIF("CloudflareAccountId", ''), "UpdatedAt"
                FROM "TunnelConfigs"
                WHERE "ProtectedApiToken" <> ''
                ORDER BY "Id"
                LIMIT 1;
                """);
            migrationBuilder.Sql("""
                INSERT INTO "CloudflareCredentials" ("ProtectedApiToken", "AccountId", "UpdatedAt")
                SELECT "ProtectedApiToken", NULL, "CreatedAt"
                FROM "DnsProviders"
                WHERE "ProtectedApiToken" <> ''
                  AND NOT EXISTS (SELECT 1 FROM "CloudflareCredentials")
                ORDER BY "IsActive" DESC, "Id"
                LIMIT 1;
                """);

            migrationBuilder.DropColumn(
                name: "CloudflareAccountId",
                table: "TunnelConfigs");

            migrationBuilder.DropColumn(
                name: "ProtectedApiToken",
                table: "TunnelConfigs");

            migrationBuilder.DropColumn(
                name: "ProtectedApiToken",
                table: "DnsProviders");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CloudflareAccountId",
                table: "TunnelConfigs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ProtectedApiToken",
                table: "TunnelConfigs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ProtectedApiToken",
                table: "DnsProviders",
                type: "text",
                nullable: false,
                defaultValue: "");

            // Hand the one token back to the tunnel and every provider.
            migrationBuilder.Sql("""
                UPDATE "TunnelConfigs" SET
                    "ProtectedApiToken" = COALESCE((SELECT "ProtectedApiToken" FROM "CloudflareCredentials" ORDER BY "Id" LIMIT 1), ''),
                    "CloudflareAccountId" = COALESCE((SELECT "AccountId" FROM "CloudflareCredentials" ORDER BY "Id" LIMIT 1), '');
                """);
            migrationBuilder.Sql("""
                UPDATE "DnsProviders" SET
                    "ProtectedApiToken" = COALESCE((SELECT "ProtectedApiToken" FROM "CloudflareCredentials" ORDER BY "Id" LIMIT 1), '');
                """);

            migrationBuilder.DropTable(
                name: "CloudflareCredentials");
        }
    }
}
