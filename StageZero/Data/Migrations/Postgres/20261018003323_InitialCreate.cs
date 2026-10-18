using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StageZero.Data.Migrations.Postgres
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
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CloudflareTokenId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ClientId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    CreatedByStageZero = table.Column<bool>(type: "boolean", nullable: false),
                    Duration = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessServiceTokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DnsProviders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ProviderType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ProtectedApiToken = table.Column<string>(type: "text", nullable: false),
                    ZoneId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DnsProviders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IpChecks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: false),
                    CheckedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastConfirmedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Confirmations = table.Column<int>(type: "integer", nullable: false),
                    IsChanged = table.Column<bool>(type: "boolean", nullable: false),
                    PreviousIpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IpChecks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TunnelConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CloudflareAccountId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CloudflareZoneId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CloudflareZoneName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ProtectedApiToken = table.Column<string>(type: "text", nullable: false),
                    TunnelId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TunnelName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TunnelConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TunnelRoutes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DomainName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ForwardScheme = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    ForwardHost = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ForwardPort = table.Column<int>(type: "integer", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AccessMode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AccessAllowedEmails = table.Column<string>(type: "text", nullable: true),
                    AccessAllowedEmailDomains = table.Column<string>(type: "text", nullable: true),
                    AccessAllowedIdpIds = table.Column<string>(type: "text", nullable: true),
                    AccessSessionDuration = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    AccessCreateServiceToken = table.Column<bool>(type: "boolean", nullable: false),
                    AccessServiceTokenName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    AccessServiceTokenId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AccessServiceTokenDuration = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    AccessApplicationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AccessIdentityPolicyId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AccessServiceTokenPolicyId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AccessSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TunnelRoutes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    EmailVerified = table.Column<bool>(type: "boolean", nullable: false),
                    EmailVerificationCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    EmailVerificationCodeExpiry = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PasswordResetCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    PasswordResetCodeExpiry = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastLoginAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    RequiresPasswordChange = table.Column<bool>(type: "boolean", nullable: false),
                    FailedLoginCount = table.Column<int>(type: "integer", nullable: false),
                    LockoutEndsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PasswordResetAttempts = table.Column<int>(type: "integer", nullable: false),
                    EmailVerificationAttempts = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DnsRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DnsProviderId = table.Column<int>(type: "integer", nullable: false),
                    RecordName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    RecordType = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    RecordId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AutoUpdate = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastIpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    Content = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true)
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
