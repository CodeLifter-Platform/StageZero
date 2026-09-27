using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StageZero.Data.Migrations
{
    /// <inheritdoc />
    public partial class CollapseIpHistoryIntoRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Confirmations",
                table: "IpChecks",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastConfirmedAt",
                table: "IpChecks",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // Fold the existing one-row-per-check history into runs: consecutive checks of the
            // same address become the run's first row, carrying the last check's time and the
            // count. (A month of a stable IP was 2,715 rows; it becomes one.)
            migrationBuilder.Sql("""
                CREATE TEMP TABLE "IpCheckRuns" AS
                SELECT "Id", "CheckedAt",
                       SUM("Starts") OVER (ORDER BY "CheckedAt", "Id") AS "Run"
                FROM (
                    SELECT "Id", "CheckedAt",
                           CASE WHEN "IpAddress" = LAG("IpAddress") OVER (ORDER BY "CheckedAt", "Id")
                                THEN 0 ELSE 1 END AS "Starts"
                    FROM "IpChecks");

                CREATE TEMP TABLE "IpCheckRunTotals" AS
                SELECT MIN("Id") AS "KeepId", MAX("CheckedAt") AS "LastAt", COUNT(*) AS "Total"
                FROM "IpCheckRuns" GROUP BY "Run";

                UPDATE "IpChecks"
                SET "LastConfirmedAt" = (SELECT "LastAt" FROM "IpCheckRunTotals" WHERE "KeepId" = "IpChecks"."Id"),
                    "Confirmations" = (SELECT "Total" FROM "IpCheckRunTotals" WHERE "KeepId" = "IpChecks"."Id")
                WHERE "Id" IN (SELECT "KeepId" FROM "IpCheckRunTotals");

                DELETE FROM "IpChecks" WHERE "Id" NOT IN (SELECT "KeepId" FROM "IpCheckRunTotals");

                DROP TABLE "IpCheckRunTotals";
                DROP TABLE "IpCheckRuns";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Confirmations",
                table: "IpChecks");

            migrationBuilder.DropColumn(
                name: "LastConfirmedAt",
                table: "IpChecks");
        }
    }
}
