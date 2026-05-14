using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderService.Migrations
{
    public partial class AddProcessedMessagesTtlCleanup : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // No schema changes required for TTL; this migration documents the intent.
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No schema changes to revert
        }
    }
}
