using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecruitmentSaaS.Migrations
{
    /// <inheritdoc />
    public partial class AddLeadAutoFollowup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AutoFollowupDelayMinutes",
                schema: "demorecruitment",
                table: "LeadFormSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "AutoFollowupEnabled",
                schema: "demorecruitment",
                table: "LeadFormSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "AutoFollowupEnabledAt",
                schema: "demorecruitment",
                table: "LeadFormSettings",
                type: "timestamp(0) without time zone",
                precision: 0,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoFollowupDelayMinutes",
                schema: "demorecruitment",
                table: "LeadFormSettings");

            migrationBuilder.DropColumn(
                name: "AutoFollowupEnabled",
                schema: "demorecruitment",
                table: "LeadFormSettings");

            migrationBuilder.DropColumn(
                name: "AutoFollowupEnabledAt",
                schema: "demorecruitment",
                table: "LeadFormSettings");
        }
    }
}
