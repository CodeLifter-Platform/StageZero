using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StageZero.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccessServiceTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CloudflareTokenId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    ClientId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    CreatedByStageZero = table.Column<bool>(type: "INTEGER", nullable: false),
                    Duration = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessServiceTokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Key = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DnsProviders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ProviderType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ApiToken = table.Column<string>(type: "TEXT", nullable: false),
                    ZoneId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DnsProviders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IpChecks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IpAddress = table.Column<string>(type: "TEXT", maxLength: 45, nullable: false),
                    CheckedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsChanged = table.Column<bool>(type: "INTEGER", nullable: false),
                    PreviousIpAddress = table.Column<string>(type: "TEXT", maxLength: 45, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IpChecks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TunnelConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CloudflareAccountId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CloudflareZoneId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CloudflareZoneName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    ProtectedApiToken = table.Column<string>(type: "TEXT", nullable: false),
                    TunnelId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    TunnelName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TunnelConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TunnelRoutes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DomainName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    ForwardScheme = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    ForwardHost = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    ForwardPort = table.Column<int>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    AccessMode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    AccessAllowedEmails = table.Column<string>(type: "TEXT", nullable: true),
                    AccessAllowedEmailDomains = table.Column<string>(type: "TEXT", nullable: true),
                    AccessAllowedIdpIds = table.Column<string>(type: "TEXT", nullable: true),
                    AccessSessionDuration = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    AccessCreateServiceToken = table.Column<bool>(type: "INTEGER", nullable: false),
                    AccessServiceTokenName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    AccessServiceTokenId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    AccessServiceTokenDuration = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    AccessApplicationId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    AccessIdentityPolicyId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    AccessServiceTokenPolicyId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    AccessSyncedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TunnelRoutes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                    EmailVerified = table.Column<bool>(type: "INTEGER", nullable: false),
                    EmailVerificationCode = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    EmailVerificationCodeExpiry = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PasswordResetCode = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    PasswordResetCodeExpiry = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastLoginAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    RequiresPasswordChange = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DnsRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DnsProviderId = table.Column<int>(type: "INTEGER", nullable: false),
                    RecordName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    RecordType = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    RecordId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    AutoUpdate = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastUpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastIpAddress = table.Column<string>(type: "TEXT", maxLength: 45, nullable: true),
                    Content = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DnsRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DnsRecords_DnsProviders_DnsProviderId",
                        column: x => x.DnsProviderId,
                        principalTable: "DnsProviders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccessServiceTokens_CloudflareTokenId",
                table: "AccessServiceTokens",
                column: "CloudflareTokenId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppSettings_Key",
                table: "AppSettings",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DnsRecords_DnsProviderId",
                table: "DnsRecords",
                column: "DnsProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_IpChecks_CheckedAt",
                table: "IpChecks",
                column: "CheckedAt");

            migrationBuilder.CreateIndex(
                name: "IX_TunnelRoutes_DomainName",
                table: "TunnelRoutes",
                column: "DomainName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TunnelRoutes_IsEnabled",
                table: "TunnelRoutes",
                column: "IsEnabled");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccessServiceTokens");

            migrationBuilder.DropTable(
                name: "AppSettings");

            migrationBuilder.DropTable(
                name: "DnsRecords");

            migrationBuilder.DropTable(
                name: "IpChecks");

            migrationBuilder.DropTable(
                name: "TunnelConfigs");

            migrationBuilder.DropTable(
                name: "TunnelRoutes");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "DnsProviders");
        }
    }
}
