using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecruitmentSaaS.Migrations
{
    /// <inheritdoc />
    public partial class AiAutomaticReplyAttribution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "RequestedById",
                schema: "demorecruitment",
                table: "AiSalesDrafts",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                IF EXISTS (SELECT 1 FROM demorecruitment."AiSalesDrafts" WHERE "RequestedById" IS NULL) THEN
                    RAISE EXCEPTION 'Reconcile automatic draft attribution before rolling back; never fabricate a human requester';
                END IF;
                END $$;
                """);
            migrationBuilder.AlterColumn<Guid>(
                name: "RequestedById",
                schema: "demorecruitment",
                table: "AiSalesDrafts",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
