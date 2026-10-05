using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecruitmentSaaS.Migrations
{
    /// <inheritdoc />
    public partial class WidenHandoffUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The WhatsApp link holds the pre-filled Arabic message; URL-encoded it often passed 500
            // characters and the whole registration failed. Allow 2000.
            migrationBuilder.Sql("""
                ALTER TABLE demorecruitment."WhatsAppHandoffs" DROP CONSTRAINT IF EXISTS "LEN_WhatsAppHandoffs_GeneratedWhatsAppUrl";
                ALTER TABLE demorecruitment."WhatsAppHandoffs" ADD CONSTRAINT "LEN_WhatsAppHandoffs_GeneratedWhatsAppUrl" CHECK (char_length("GeneratedWhatsAppUrl"::text) <= 2000);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE demorecruitment."WhatsAppHandoffs" DROP CONSTRAINT IF EXISTS "LEN_WhatsAppHandoffs_GeneratedWhatsAppUrl";
                ALTER TABLE demorecruitment."WhatsAppHandoffs" ADD CONSTRAINT "LEN_WhatsAppHandoffs_GeneratedWhatsAppUrl" CHECK (char_length("GeneratedWhatsAppUrl"::text) <= 500);
                """);
        }
    }
}
