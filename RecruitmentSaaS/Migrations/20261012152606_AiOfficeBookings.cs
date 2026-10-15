using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecruitmentSaaS.Migrations
{
    /// <inheritdoc />
    public partial class AiOfficeBookings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiOfficePolicies",
                schema: "demorecruitment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WhatsAppAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    OpenDaysMask = table.Column<int>(type: "integer", nullable: false),
                    OpensAtMinute = table.Column<int>(type: "integer", nullable: false),
                    ClosesAtMinute = table.Column<int>(type: "integer", nullable: false),
                    ValidUntil = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    SourceReference = table.Column<string>(type: "citext", maxLength: 500, nullable: false),
                    ApprovedById = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiOfficePolicies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiOfficePolicies_Branches_BranchId",
                        column: x => x.BranchId,
                        principalSchema: "demorecruitment",
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiOfficePolicies_Users_ApprovedById",
                        column: x => x.ApprovedById,
                        principalSchema: "demorecruitment",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiOfficePolicies_WhatsAppAccounts_WhatsAppAccountId",
                        column: x => x.WhatsAppAccountId,
                        principalSchema: "demorecruitment",
                        principalTable: "WhatsAppAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AiOfficeBookings",
                schema: "demorecruitment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    LeadId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamManagerId = table.Column<Guid>(type: "uuid", nullable: true),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uuid", nullable: false),
                    PolicyVersion = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<byte>(type: "smallint", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    ArrivalLocal = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Purpose = table.Column<string>(type: "citext", maxLength: 500, nullable: false),
                    ConfirmationMessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConfirmationVoiceTurnId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConfirmationFingerprint = table.Column<string>(type: "text", maxLength: 64, nullable: false),
                    ReminderId = table.Column<Guid>(type: "uuid", nullable: true),
                    VerifiedVisitId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedById = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiOfficeBookings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiOfficeBookings_AiOfficePolicies_PolicyId",
                        column: x => x.PolicyId,
                        principalSchema: "demorecruitment",
                        principalTable: "AiOfficePolicies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiOfficeBookings_AiSalesAgents_AgentId",
                        column: x => x.AgentId,
                        principalSchema: "demorecruitment",
                        principalTable: "AiSalesAgents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiOfficeBookings_AiVoiceTurns_ConfirmationVoiceTurnId",
                        column: x => x.ConfirmationVoiceTurnId,
                        principalSchema: "demorecruitment",
                        principalTable: "AiVoiceTurns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiOfficeBookings_Branches_BranchId",
                        column: x => x.BranchId,
                        principalSchema: "demorecruitment",
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiOfficeBookings_FollowUpReminders_ReminderId",
                        column: x => x.ReminderId,
                        principalSchema: "demorecruitment",
                        principalTable: "FollowUpReminders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiOfficeBookings_LeadVisits_VerifiedVisitId",
                        column: x => x.VerifiedVisitId,
                        principalSchema: "demorecruitment",
                        principalTable: "LeadVisits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiOfficeBookings_Leads_LeadId",
                        column: x => x.LeadId,
                        principalSchema: "demorecruitment",
                        principalTable: "Leads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiOfficeBookings_Users_ReviewedById",
                        column: x => x.ReviewedById,
                        principalSchema: "demorecruitment",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiOfficeBookings_Users_TeamManagerId",
                        column: x => x.TeamManagerId,
                        principalSchema: "demorecruitment",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiOfficeBookings_WhatsAppConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalSchema: "demorecruitment",
                        principalTable: "WhatsAppConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiOfficeBookings_WhatsAppMessages_ConfirmationMessageId",
                        column: x => x.ConfirmationMessageId,
                        principalSchema: "demorecruitment",
                        principalTable: "WhatsAppMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AiOfficeBookingEvents",
                schema: "demorecruitment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    BookingId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Action = table.Column<string>(type: "citext", maxLength: 40, nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    At = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ArrivalLocal = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Purpose = table.Column<string>(type: "citext", maxLength: 500, nullable: false),
                    ConfirmationMessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConfirmationFingerprint = table.Column<string>(type: "text", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "citext", maxLength: 500, nullable: false),
                    FunnelHistoryId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiOfficeBookingEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiOfficeBookingEvents_AiOfficeBookings_BookingId",
                        column: x => x.BookingId,
                        principalSchema: "demorecruitment",
                        principalTable: "AiOfficeBookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiOfficeBookingEvents_Branches_BranchId",
                        column: x => x.BranchId,
                        principalSchema: "demorecruitment",
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiOfficeBookingEvents_LeadFunnelHistory_FunnelHistoryId",
                        column: x => x.FunnelHistoryId,
                        principalSchema: "demorecruitment",
                        principalTable: "LeadFunnelHistory",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiOfficeBookingEvents_Users_ActorId",
                        column: x => x.ActorId,
                        principalSchema: "demorecruitment",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiOfficeBookingEvents_WhatsAppMessages_ConfirmationMessageId",
                        column: x => x.ConfirmationMessageId,
                        principalSchema: "demorecruitment",
                        principalTable: "WhatsAppMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiOfficeBookingEvents_ActorId",
                schema: "demorecruitment",
                table: "AiOfficeBookingEvents",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_AiOfficeBookingEvents_BookingId_Version",
                schema: "demorecruitment",
                table: "AiOfficeBookingEvents",
                columns: new[] { "BookingId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiOfficeBookingEvents_BranchId",
                schema: "demorecruitment",
                table: "AiOfficeBookingEvents",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_AiOfficeBookingEvents_ConfirmationMessageId",
                schema: "demorecruitment",
                table: "AiOfficeBookingEvents",
                column: "ConfirmationMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_AiOfficeBookingEvents_FunnelHistoryId",
                schema: "demorecruitment",
                table: "AiOfficeBookingEvents",
                column: "FunnelHistoryId");

            migrationBuilder.CreateIndex(
                name: "IX_AiOfficeBookings_AgentId",
                schema: "demorecruitment",
                table: "AiOfficeBookings",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_AiOfficeBookings_BranchId",
                schema: "demorecruitment",
                table: "AiOfficeBookings",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_AiOfficeBookings_ConfirmationMessageId",
                schema: "demorecruitment",
                table: "AiOfficeBookings",
                column: "ConfirmationMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_AiOfficeBookings_ConfirmationVoiceTurnId",
                schema: "demorecruitment",
                table: "AiOfficeBookings",
                column: "ConfirmationVoiceTurnId");

            migrationBuilder.CreateIndex(
                name: "IX_AiOfficeBookings_ConversationId",
                schema: "demorecruitment",
                table: "AiOfficeBookings",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_AiOfficeBookings_LeadId",
                schema: "demorecruitment",
                table: "AiOfficeBookings",
                column: "LeadId",
                unique: true,
                filter: "\"Status\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_AiOfficeBookings_PolicyId",
                schema: "demorecruitment",
                table: "AiOfficeBookings",
                column: "PolicyId");

            migrationBuilder.CreateIndex(
                name: "IX_AiOfficeBookings_ReminderId",
                schema: "demorecruitment",
                table: "AiOfficeBookings",
                column: "ReminderId");

            migrationBuilder.CreateIndex(
                name: "IX_AiOfficeBookings_ReviewedById",
                schema: "demorecruitment",
                table: "AiOfficeBookings",
                column: "ReviewedById");

            migrationBuilder.CreateIndex(
                name: "IX_AiOfficeBookings_TeamManagerId",
                schema: "demorecruitment",
                table: "AiOfficeBookings",
                column: "TeamManagerId");

            migrationBuilder.CreateIndex(
                name: "IX_AiOfficeBookings_TenantId_CreatedAt",
                schema: "demorecruitment",
                table: "AiOfficeBookings",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AiOfficeBookings_VerifiedVisitId",
                schema: "demorecruitment",
                table: "AiOfficeBookings",
                column: "VerifiedVisitId");

            migrationBuilder.CreateIndex(
                name: "IX_AiOfficePolicies_ApprovedById",
                schema: "demorecruitment",
                table: "AiOfficePolicies",
                column: "ApprovedById");

            migrationBuilder.CreateIndex(
                name: "IX_AiOfficePolicies_BranchId",
                schema: "demorecruitment",
                table: "AiOfficePolicies",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_AiOfficePolicies_TenantId_WhatsAppAccountId_BranchId",
                schema: "demorecruitment",
                table: "AiOfficePolicies",
                columns: new[] { "TenantId", "WhatsAppAccountId", "BranchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiOfficePolicies_WhatsAppAccountId",
                schema: "demorecruitment",
                table: "AiOfficePolicies",
                column: "WhatsAppAccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiOfficeBookingEvents",
                schema: "demorecruitment");

            migrationBuilder.DropTable(
                name: "AiOfficeBookings",
                schema: "demorecruitment");

            migrationBuilder.DropTable(
                name: "AiOfficePolicies",
                schema: "demorecruitment");
        }
    }
}
