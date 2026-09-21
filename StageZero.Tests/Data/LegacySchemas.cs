namespace StageZero.Tests.Data;

/// <summary>
/// Real pre-migrations schemas, dumped with <c>.schema</c> from two installs (no data).
/// Both still carry the retired ProxyHosts table and lack the tunnel tables.
/// </summary>
internal static class LegacySchemas
{
    /// <summary>
    /// January 2026: users keyed on Username, Email optional; the reset-code columns were
    /// bolted on by ALTER TABLE.
    /// </summary>
    public const string January2026 = """
        CREATE TABLE "AppSettings" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_AppSettings" PRIMARY KEY AUTOINCREMENT,
            "Key" TEXT NOT NULL, "Value" TEXT NOT NULL, "UpdatedAt" TEXT NOT NULL);
        CREATE TABLE "DnsProviders" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_DnsProviders" PRIMARY KEY AUTOINCREMENT,
            "Name" TEXT NOT NULL, "ProviderType" TEXT NOT NULL, "ApiToken" TEXT NOT NULL,
            "ZoneId" TEXT NULL, "IsActive" INTEGER NOT NULL, "CreatedAt" TEXT NOT NULL,
            "LastUsedAt" TEXT NULL);
        CREATE TABLE "IpChecks" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_IpChecks" PRIMARY KEY AUTOINCREMENT,
            "IpAddress" TEXT NOT NULL, "CheckedAt" TEXT NOT NULL, "IsChanged" INTEGER NOT NULL,
            "PreviousIpAddress" TEXT NULL);
        CREATE TABLE "ProxyHosts" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_ProxyHosts" PRIMARY KEY AUTOINCREMENT,
            "DomainName" TEXT NOT NULL, "ForwardScheme" TEXT NOT NULL, "ForwardHost" TEXT NOT NULL,
            "ForwardPort" INTEGER NOT NULL, "IsEnabled" INTEGER NOT NULL,
            "CreatedAt" TEXT NOT NULL, "UpdatedAt" TEXT NOT NULL, "Notes" TEXT NULL);
        CREATE TABLE "Users" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_Users" PRIMARY KEY AUTOINCREMENT,
            "Username" TEXT NOT NULL, "PasswordHash" TEXT NOT NULL, "Email" TEXT NULL,
            "EmailVerified" INTEGER NOT NULL, "EmailVerificationCode" TEXT NULL,
            "EmailVerificationCodeExpiry" TEXT NULL, "CreatedAt" TEXT NOT NULL,
            "LastLoginAt" TEXT NULL, "IsActive" INTEGER NOT NULL,
            "RequiresPasswordChange" INTEGER NOT NULL
        , PasswordResetCode TEXT, PasswordResetCodeExpiry TEXT);
        CREATE TABLE "DnsRecords" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_DnsRecords" PRIMARY KEY AUTOINCREMENT,
            "DnsProviderId" INTEGER NOT NULL, "RecordName" TEXT NOT NULL, "RecordType" TEXT NOT NULL,
            "RecordId" TEXT NULL, "AutoUpdate" INTEGER NOT NULL, "CreatedAt" TEXT NOT NULL,
            "LastUpdatedAt" TEXT NULL, "LastIpAddress" TEXT NULL, "Content" TEXT NULL,
            CONSTRAINT "FK_DnsRecords_DnsProviders_DnsProviderId" FOREIGN KEY ("DnsProviderId")
                REFERENCES "DnsProviders" ("Id") ON DELETE CASCADE);
        CREATE UNIQUE INDEX "IX_AppSettings_Key" ON "AppSettings" ("Key");
        CREATE INDEX "IX_DnsRecords_DnsProviderId" ON "DnsRecords" ("DnsProviderId");
        CREATE INDEX "IX_IpChecks_CheckedAt" ON "IpChecks" ("CheckedAt");
        CREATE UNIQUE INDEX "IX_Users_Username" ON "Users" ("Username");
        """;

    /// <summary>June 2026: the current Users shape, but no tunnel tables yet.</summary>
    public const string June2026 = """
        CREATE TABLE "AppSettings" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_AppSettings" PRIMARY KEY AUTOINCREMENT,
            "Key" TEXT NOT NULL, "Value" TEXT NOT NULL, "UpdatedAt" TEXT NOT NULL);
        CREATE TABLE "DnsProviders" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_DnsProviders" PRIMARY KEY AUTOINCREMENT,
            "Name" TEXT NOT NULL, "ProviderType" TEXT NOT NULL, "ApiToken" TEXT NOT NULL,
            "ZoneId" TEXT NULL, "IsActive" INTEGER NOT NULL, "CreatedAt" TEXT NOT NULL,
            "LastUsedAt" TEXT NULL);
        CREATE TABLE "IpChecks" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_IpChecks" PRIMARY KEY AUTOINCREMENT,
            "IpAddress" TEXT NOT NULL, "CheckedAt" TEXT NOT NULL, "IsChanged" INTEGER NOT NULL,
            "PreviousIpAddress" TEXT NULL);
        CREATE TABLE "ProxyHosts" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_ProxyHosts" PRIMARY KEY AUTOINCREMENT,
            "DomainName" TEXT NOT NULL, "ForwardScheme" TEXT NOT NULL, "ForwardHost" TEXT NOT NULL,
            "ForwardPort" INTEGER NOT NULL, "IsEnabled" INTEGER NOT NULL,
            "CreatedAt" TEXT NOT NULL, "UpdatedAt" TEXT NOT NULL, "Notes" TEXT NULL);
        CREATE TABLE "Users" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_Users" PRIMARY KEY AUTOINCREMENT,
            "Email" TEXT NOT NULL, "PasswordHash" TEXT NOT NULL, "EmailVerified" INTEGER NOT NULL,
            "EmailVerificationCode" TEXT NULL, "EmailVerificationCodeExpiry" TEXT NULL,
            "PasswordResetCode" TEXT NULL, "PasswordResetCodeExpiry" TEXT NULL,
            "CreatedAt" TEXT NOT NULL, "LastLoginAt" TEXT NULL, "IsActive" INTEGER NOT NULL,
            "RequiresPasswordChange" INTEGER NOT NULL);
        CREATE TABLE "DnsRecords" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_DnsRecords" PRIMARY KEY AUTOINCREMENT,
            "DnsProviderId" INTEGER NOT NULL, "RecordName" TEXT NOT NULL, "RecordType" TEXT NOT NULL,
            "RecordId" TEXT NULL, "AutoUpdate" INTEGER NOT NULL, "CreatedAt" TEXT NOT NULL,
            "LastUpdatedAt" TEXT NULL, "LastIpAddress" TEXT NULL, "Content" TEXT NULL,
            CONSTRAINT "FK_DnsRecords_DnsProviders_DnsProviderId" FOREIGN KEY ("DnsProviderId")
                REFERENCES "DnsProviders" ("Id") ON DELETE CASCADE);
        CREATE UNIQUE INDEX "IX_AppSettings_Key" ON "AppSettings" ("Key");
        CREATE INDEX "IX_DnsRecords_DnsProviderId" ON "DnsRecords" ("DnsProviderId");
        CREATE INDEX "IX_IpChecks_CheckedAt" ON "IpChecks" ("CheckedAt");
        CREATE UNIQUE INDEX "IX_Users_Email" ON "Users" ("Email");
        """;

    /// <summary>Rows every legacy fixture shares (both shapes agree on these tables).</summary>
    public const string CommonRows = """
        INSERT INTO AppSettings VALUES (1, 'IpCheckIntervalSeconds', '300', '2026-01-02 03:04:05');
        INSERT INTO DnsProviders VALUES (1, 'Home', 'Cloudflare', 'test-token', 'zone-1', 1, '2026-01-02 03:04:05', NULL);
        INSERT INTO DnsRecords VALUES (1, 1, 'home.example.com', 'A', 'rec-1', 1, '2026-01-02 03:04:05', NULL, '203.0.113.7', NULL);
        INSERT INTO IpChecks VALUES (1, '203.0.113.7', '2026-01-02 03:04:05', 1, NULL);
        INSERT INTO ProxyHosts VALUES (1, 'old.example.com', 'http', '192.168.1.10', 80, 1, '2026-01-02 03:04:05', '2026-01-02 03:04:05', NULL);
        """;
}
