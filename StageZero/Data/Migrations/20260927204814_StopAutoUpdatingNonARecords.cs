using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StageZero.Data.Migrations
{
    /// <inheritdoc />
    public partial class StopAutoUpdatingNonARecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Only A records can be kept pointed at the (IPv4) public IP. AAAA and CNAME
            // records were flagged for auto-update anyway — imported that way, or because the
            // add dialog's checkbox was ignored — and the UI said so while nothing happened.
            migrationBuilder.Sql("UPDATE \"DnsRecords\" SET \"AutoUpdate\" = 0 WHERE \"RecordType\" <> 'A';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo: the old flags described updates that never happened.
        }
    }
}
