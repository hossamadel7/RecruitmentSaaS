using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecruitmentSaaS.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// This project has no prior EF migration history (it's DB-first with hand-written SQL).
    /// The auto-generated version of this migration therefore tried to re-create all ~30 existing
    /// tables. This file has been hand-trimmed to contain only the new WhatsApp Shared Inbox tables;
    /// the Designer.cs / ModelSnapshot.cs files still hold the full current model, which is correct.
    /// </remarks>
    public partial class InitialWhatsAppInbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WhatsAppAccounts",
                schema: "demorecruitment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newsequentialid())"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DisplayPhoneNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PhoneNumberId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    WabaId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    AssignedSalesAgentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false, defaultValueSql: "(sysutcdatetime())"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_demorecruitment_WA_Acc", x => x.Id);
                    table.ForeignKey(
                        name: "FK_demorecruitment_WA_Acc_Agent",
                        column: x => x.AssignedSalesAgentId,
                        principalSchema: "demorecruitment",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "WhatsAppContacts",
                schema: "demorecruitment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newsequentialid())"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    WhatsAppPhoneNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    WhatsAppUserId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false, defaultValueSql: "(sysutcdatetime())"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    LastSeenAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_demorecruitment_WA_Cnt", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WhatsAppConversations",
                schema: "demorecruitment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newsequentialid())"),
                    ContactId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WhatsAppAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignedSalesAgentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LeadId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false, defaultValue: (byte)1),
                    LeadStage = table.Column<byte>(type: "tinyint", nullable: false, defaultValue: (byte)1),
                    LostReason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    UnreadCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    OpenedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false, defaultValueSql: "(sysutcdatetime())"),
                    LastMessageAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false, defaultValueSql: "(sysutcdatetime())"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_demorecruitment_WA_Cnv", x => x.Id);
                    table.ForeignKey(
                        name: "FK_demorecruitment_WA_Cnv_Acc",
                        column: x => x.WhatsAppAccountId,
                        principalSchema: "demorecruitment",
                        principalTable: "WhatsAppAccounts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_demorecruitment_WA_Cnv_Agent",
                        column: x => x.AssignedSalesAgentId,
                        principalSchema: "demorecruitment",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_demorecruitment_WA_Cnv_Cnt",
                        column: x => x.ContactId,
                        principalSchema: "demorecruitment",
                        principalTable: "WhatsAppContacts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_demorecruitment_WA_Cnv_Lead",
                        column: x => x.LeadId,
                        principalSchema: "demorecruitment",
                        principalTable: "Leads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "WhatsAppMessages",
                schema: "demorecruitment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newsequentialid())"),
                    ConversationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WhatsAppAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WhatsAppMessageId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Direction = table.Column<byte>(type: "tinyint", nullable: false),
                    MessageType = table.Column<byte>(type: "tinyint", nullable: false, defaultValue: (byte)1),
                    TextBody = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    MediaId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    MediaUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SenderUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReplyToMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    ErrorCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    WhatsAppTimestamp = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false, defaultValueSql: "(sysutcdatetime())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_demorecruitment_WA_Msg", x => x.Id);
                    table.ForeignKey(
                        name: "FK_demorecruitment_WA_Msg_Acc",
                        column: x => x.WhatsAppAccountId,
                        principalSchema: "demorecruitment",
                        principalTable: "WhatsAppAccounts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_demorecruitment_WA_Msg_Cnv",
                        column: x => x.ConversationId,
                        principalSchema: "demorecruitment",
                        principalTable: "WhatsAppConversations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_demorecruitment_WA_Msg_Reply",
                        column: x => x.ReplyToMessageId,
                        principalSchema: "demorecruitment",
                        principalTable: "WhatsAppMessages",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_demorecruitment_WA_Msg_Sender",
                        column: x => x.SenderUserId,
                        principalSchema: "demorecruitment",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "WhatsAppHandoffs",
                schema: "demorecruitment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newsequentialid())"),
                    LeadId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WhatsAppAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignedSalesAgentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReferenceCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    GeneratedWhatsAppUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false, defaultValue: (byte)1),
                    ConversationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false, defaultValueSql: "(sysutcdatetime())"),
                    SentAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    ConnectedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    ExpiredAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_demorecruitment_WA_Hnd", x => x.Id);
                    table.ForeignKey(
                        name: "FK_demorecruitment_WA_Hnd_Acc",
                        column: x => x.WhatsAppAccountId,
                        principalSchema: "demorecruitment",
                        principalTable: "WhatsAppAccounts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_demorecruitment_WA_Hnd_Agent",
                        column: x => x.AssignedSalesAgentId,
                        principalSchema: "demorecruitment",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_demorecruitment_WA_Hnd_Cnv",
                        column: x => x.ConversationId,
                        principalSchema: "demorecruitment",
                        principalTable: "WhatsAppConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_demorecruitment_WA_Hnd_Lead",
                        column: x => x.LeadId,
                        principalSchema: "demorecruitment",
                        principalTable: "Leads",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ConversationNotes",
                schema: "demorecruitment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newsequentialid())"),
                    ConversationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false, defaultValueSql: "(sysutcdatetime())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_demorecruitment_WA_Note", x => x.Id);
                    table.ForeignKey(
                        name: "FK_demorecruitment_WA_Note_Author",
                        column: x => x.AuthorId,
                        principalSchema: "demorecruitment",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_demorecruitment_WA_Note_Cnv",
                        column: x => x.ConversationId,
                        principalSchema: "demorecruitment",
                        principalTable: "WhatsAppConversations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ConversationFollowUps",
                schema: "demorecruitment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newsequentialid())"),
                    ConversationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignedToId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DueAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false, defaultValue: (byte)1),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    CompletedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false, defaultValueSql: "(sysutcdatetime())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_demorecruitment_WA_FU", x => x.Id);
                    table.ForeignKey(
                        name: "FK_demorecruitment_WA_FU_Assignee",
                        column: x => x.AssignedToId,
                        principalSchema: "demorecruitment",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_demorecruitment_WA_FU_Cnv",
                        column: x => x.ConversationId,
                        principalSchema: "demorecruitment",
                        principalTable: "WhatsAppConversations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_demorecruitment_WA_FU_CompletedBy",
                        column: x => x.CompletedById,
                        principalSchema: "demorecruitment",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_demorecruitment_WA_FU_CreatedBy",
                        column: x => x.CreatedById,
                        principalSchema: "demorecruitment",
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            // ── Indexes ──────────────────────────────────────────────────────

            migrationBuilder.CreateIndex(
                name: "IX_demorecruitment_WA_Acc_Active",
                schema: "demorecruitment",
                table: "WhatsAppAccounts",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppAccounts_AssignedSalesAgentId",
                schema: "demorecruitment",
                table: "WhatsAppAccounts",
                column: "AssignedSalesAgentId");

            migrationBuilder.CreateIndex(
                name: "UQ_demorecruitment_WA_Acc_PhoneNumberId",
                schema: "demorecruitment",
                table: "WhatsAppAccounts",
                column: "PhoneNumberId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_demorecruitment_WA_Cnt_Phone",
                schema: "demorecruitment",
                table: "WhatsAppContacts",
                column: "WhatsAppPhoneNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_demorecruitment_WA_Cnv_Acc",
                schema: "demorecruitment",
                table: "WhatsAppConversations",
                column: "WhatsAppAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_demorecruitment_WA_Cnv_Agent",
                schema: "demorecruitment",
                table: "WhatsAppConversations",
                column: "AssignedSalesAgentId");

            migrationBuilder.CreateIndex(
                name: "IX_demorecruitment_WA_Cnv_LastMsg",
                schema: "demorecruitment",
                table: "WhatsAppConversations",
                column: "LastMessageAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_demorecruitment_WA_Cnv_Lead",
                schema: "demorecruitment",
                table: "WhatsAppConversations",
                column: "LeadId");

            migrationBuilder.CreateIndex(
                name: "IX_demorecruitment_WA_Cnv_LeadStage",
                schema: "demorecruitment",
                table: "WhatsAppConversations",
                column: "LeadStage");

            migrationBuilder.CreateIndex(
                name: "IX_demorecruitment_WA_Cnv_Status",
                schema: "demorecruitment",
                table: "WhatsAppConversations",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "UQ_demorecruitment_WA_Cnv_Cnt_Acc",
                schema: "demorecruitment",
                table: "WhatsAppConversations",
                columns: new[] { "ContactId", "WhatsAppAccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_demorecruitment_WA_Msg_Acc",
                schema: "demorecruitment",
                table: "WhatsAppMessages",
                column: "WhatsAppAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_demorecruitment_WA_Msg_Cnv",
                schema: "demorecruitment",
                table: "WhatsAppMessages",
                columns: new[] { "ConversationId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_demorecruitment_WA_Msg_Status",
                schema: "demorecruitment",
                table: "WhatsAppMessages",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessages_ReplyToMessageId",
                schema: "demorecruitment",
                table: "WhatsAppMessages",
                column: "ReplyToMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessages_SenderUserId",
                schema: "demorecruitment",
                table: "WhatsAppMessages",
                column: "SenderUserId");

            migrationBuilder.CreateIndex(
                name: "UQ_demorecruitment_WA_Msg_WamId",
                schema: "demorecruitment",
                table: "WhatsAppMessages",
                column: "WhatsAppMessageId",
                unique: true,
                filter: "([WhatsAppMessageId] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_demorecruitment_WA_Hnd_Lead",
                schema: "demorecruitment",
                table: "WhatsAppHandoffs",
                column: "LeadId");

            migrationBuilder.CreateIndex(
                name: "IX_demorecruitment_WA_Hnd_Status",
                schema: "demorecruitment",
                table: "WhatsAppHandoffs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppHandoffs_AssignedSalesAgentId",
                schema: "demorecruitment",
                table: "WhatsAppHandoffs",
                column: "AssignedSalesAgentId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppHandoffs_ConversationId",
                schema: "demorecruitment",
                table: "WhatsAppHandoffs",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppHandoffs_WhatsAppAccountId",
                schema: "demorecruitment",
                table: "WhatsAppHandoffs",
                column: "WhatsAppAccountId");

            migrationBuilder.CreateIndex(
                name: "UQ_demorecruitment_WA_Hnd_Ref",
                schema: "demorecruitment",
                table: "WhatsAppHandoffs",
                column: "ReferenceCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConversationNotes_AuthorId",
                schema: "demorecruitment",
                table: "ConversationNotes",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_demorecruitment_WA_Note_Cnv",
                schema: "demorecruitment",
                table: "ConversationNotes",
                columns: new[] { "ConversationId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationFollowUps_CompletedById",
                schema: "demorecruitment",
                table: "ConversationFollowUps",
                column: "CompletedById");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationFollowUps_CreatedById",
                schema: "demorecruitment",
                table: "ConversationFollowUps",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_demorecruitment_WA_FU_Assignee",
                schema: "demorecruitment",
                table: "ConversationFollowUps",
                columns: new[] { "AssignedToId", "Status", "DueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_demorecruitment_WA_FU_Cnv",
                schema: "demorecruitment",
                table: "ConversationFollowUps",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_demorecruitment_WA_FU_Due",
                schema: "demorecruitment",
                table: "ConversationFollowUps",
                column: "DueAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ConversationFollowUps", schema: "demorecruitment");
            migrationBuilder.DropTable(name: "ConversationNotes", schema: "demorecruitment");
            migrationBuilder.DropTable(name: "WhatsAppHandoffs", schema: "demorecruitment");
            migrationBuilder.DropTable(name: "WhatsAppMessages", schema: "demorecruitment");
            migrationBuilder.DropTable(name: "WhatsAppConversations", schema: "demorecruitment");
            migrationBuilder.DropTable(name: "WhatsAppContacts", schema: "demorecruitment");
            migrationBuilder.DropTable(name: "WhatsAppAccounts", schema: "demorecruitment");
        }
    }
}
