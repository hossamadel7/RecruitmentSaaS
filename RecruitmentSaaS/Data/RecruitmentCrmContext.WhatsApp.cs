using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Models.Entities;

namespace RecruitmentSaaS.Data;

// Extends the scaffolded RecruitmentCrmContext with the WhatsApp Shared Inbox module.
// Kept in its own partial file so the DB-first scaffolded file never needs manual edits.
public partial class RecruitmentCrmContext
{
    public virtual DbSet<WhatsAppAccount> WhatsAppAccounts { get; set; } = null!;

    public virtual DbSet<WhatsAppContact> WhatsAppContacts { get; set; } = null!;

    public virtual DbSet<WhatsAppConversation> WhatsAppConversations { get; set; } = null!;

    public virtual DbSet<WhatsAppMessage> WhatsAppMessages { get; set; } = null!;

    public virtual DbSet<WhatsAppHandoff> WhatsAppHandoffs { get; set; } = null!;

    public virtual DbSet<ConversationNote> ConversationNotes { get; set; } = null!;

    public virtual DbSet<ConversationFollowUp> ConversationFollowUps { get; set; } = null!;

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WhatsAppAccount>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_WA_Acc");

            entity.ToTable("WhatsAppAccounts", "demorecruitment");

            entity.HasIndex(e => e.PhoneNumberId, "UQ_demorecruitment_WA_Acc_PhoneNumberId").IsUnique();

            entity.HasIndex(e => e.IsActive, "IX_demorecruitment_WA_Acc_Active");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.Name).HasMaxLength(200);
            entity.Property(e => e.DisplayPhoneNumber).HasMaxLength(30);
            entity.Property(e => e.PhoneNumberId).HasMaxLength(50);
            entity.Property(e => e.WabaId).HasMaxLength(50);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.CreatedAt).HasPrecision(0).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.UpdatedAt).HasPrecision(0);

            entity.HasOne(d => d.AssignedSalesAgent).WithMany()
                .HasForeignKey(d => d.AssignedSalesAgentId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_demorecruitment_WA_Acc_Agent");
        });

        modelBuilder.Entity<WhatsAppContact>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_WA_Cnt");

            entity.ToTable("WhatsAppContacts", "demorecruitment");

            entity.HasIndex(e => e.WhatsAppPhoneNumber, "UQ_demorecruitment_WA_Cnt_Phone").IsUnique();

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.Name).HasMaxLength(200);
            entity.Property(e => e.WhatsAppPhoneNumber).HasMaxLength(30);
            entity.Property(e => e.WhatsAppUserId).HasMaxLength(50);
            entity.Property(e => e.CreatedAt).HasPrecision(0).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.UpdatedAt).HasPrecision(0);
            entity.Property(e => e.LastSeenAt).HasPrecision(0);
        });

        modelBuilder.Entity<WhatsAppConversation>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_WA_Cnv");

            entity.ToTable("WhatsAppConversations", "demorecruitment");

            entity.HasIndex(e => new { e.ContactId, e.WhatsAppAccountId }, "UQ_demorecruitment_WA_Cnv_Cnt_Acc").IsUnique();

            entity.HasIndex(e => e.WhatsAppAccountId, "IX_demorecruitment_WA_Cnv_Acc");
            entity.HasIndex(e => e.AssignedSalesAgentId, "IX_demorecruitment_WA_Cnv_Agent");
            entity.HasIndex(e => e.LastMessageAt, "IX_demorecruitment_WA_Cnv_LastMsg").IsDescending();
            entity.HasIndex(e => e.Status, "IX_demorecruitment_WA_Cnv_Status");
            entity.HasIndex(e => e.LeadStage, "IX_demorecruitment_WA_Cnv_LeadStage");
            entity.HasIndex(e => e.LeadId, "IX_demorecruitment_WA_Cnv_Lead");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.Status).HasDefaultValue((byte)ConversationStatus.New);
            entity.Property(e => e.LeadStage).HasDefaultValue((byte)Models.Entities.LeadStage.New);
            entity.Property(e => e.LostReason).HasMaxLength(200);
            entity.Property(e => e.UnreadCount).HasDefaultValue(0);
            entity.Property(e => e.OpenedAt).HasPrecision(0).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.LastMessageAt).HasPrecision(0);
            entity.Property(e => e.ClosedAt).HasPrecision(0);
            entity.Property(e => e.CreatedAt).HasPrecision(0).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.UpdatedAt).HasPrecision(0);

            entity.HasOne(d => d.Contact).WithMany(p => p.WhatsAppConversations)
                .HasForeignKey(d => d.ContactId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_WA_Cnv_Cnt");

            entity.HasOne(d => d.WhatsAppAccount).WithMany(p => p.WhatsAppConversations)
                .HasForeignKey(d => d.WhatsAppAccountId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_WA_Cnv_Acc");

            entity.HasOne(d => d.AssignedSalesAgent).WithMany()
                .HasForeignKey(d => d.AssignedSalesAgentId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_demorecruitment_WA_Cnv_Agent");

            entity.HasOne(d => d.Lead).WithMany()
                .HasForeignKey(d => d.LeadId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_demorecruitment_WA_Cnv_Lead");
        });

        modelBuilder.Entity<WhatsAppMessage>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_WA_Msg");

            entity.ToTable("WhatsAppMessages", "demorecruitment");

            entity.HasIndex(e => e.WhatsAppMessageId, "UQ_demorecruitment_WA_Msg_WamId")
                .IsUnique()
                .HasFilter("([WhatsAppMessageId] IS NOT NULL)");

            entity.HasIndex(e => new { e.ConversationId, e.CreatedAt }, "IX_demorecruitment_WA_Msg_Cnv").IsDescending(false, true);
            entity.HasIndex(e => e.WhatsAppAccountId, "IX_demorecruitment_WA_Msg_Acc");
            entity.HasIndex(e => e.Status, "IX_demorecruitment_WA_Msg_Status");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.WhatsAppMessageId).HasMaxLength(100);
            entity.Property(e => e.MessageType).HasDefaultValue((byte)WhatsAppMessageType.Text);
            entity.Property(e => e.TextBody).HasMaxLength(4000);
            entity.Property(e => e.MediaId).HasMaxLength(200);
            entity.Property(e => e.MediaUrl).HasMaxLength(1000);
            entity.Property(e => e.ErrorCode).HasMaxLength(50);
            entity.Property(e => e.ErrorMessage).HasMaxLength(500);
            entity.Property(e => e.WhatsAppTimestamp).HasPrecision(0);
            entity.Property(e => e.CreatedAt).HasPrecision(0).HasDefaultValueSql("(sysutcdatetime())");

            entity.HasOne(d => d.Conversation).WithMany(p => p.WhatsAppMessages)
                .HasForeignKey(d => d.ConversationId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_WA_Msg_Cnv");

            entity.HasOne(d => d.WhatsAppAccount).WithMany(p => p.WhatsAppMessages)
                .HasForeignKey(d => d.WhatsAppAccountId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_WA_Msg_Acc");

            entity.HasOne(d => d.SenderUser).WithMany()
                .HasForeignKey(d => d.SenderUserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_demorecruitment_WA_Msg_Sender");

            entity.HasOne(d => d.ReplyToMessage).WithMany()
                .HasForeignKey(d => d.ReplyToMessageId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_demorecruitment_WA_Msg_Reply");
        });

        modelBuilder.Entity<WhatsAppHandoff>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_WA_Hnd");

            entity.ToTable("WhatsAppHandoffs", "demorecruitment");

            entity.HasIndex(e => e.ReferenceCode, "UQ_demorecruitment_WA_Hnd_Ref").IsUnique();
            entity.HasIndex(e => e.LeadId, "IX_demorecruitment_WA_Hnd_Lead");
            entity.HasIndex(e => e.Status, "IX_demorecruitment_WA_Hnd_Status");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.ReferenceCode).HasMaxLength(20);
            entity.Property(e => e.GeneratedWhatsAppUrl).HasMaxLength(500);
            entity.Property(e => e.Status).HasDefaultValue((byte)HandoffStatus.Generated);
            entity.Property(e => e.CreatedAt).HasPrecision(0).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.SentAt).HasPrecision(0);
            entity.Property(e => e.ConnectedAt).HasPrecision(0);
            entity.Property(e => e.ExpiredAt).HasPrecision(0);

            entity.HasOne(d => d.Lead).WithMany()
                .HasForeignKey(d => d.LeadId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_WA_Hnd_Lead");

            entity.HasOne(d => d.WhatsAppAccount).WithMany(p => p.WhatsAppHandoffs)
                .HasForeignKey(d => d.WhatsAppAccountId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_WA_Hnd_Acc");

            entity.HasOne(d => d.AssignedSalesAgent).WithMany()
                .HasForeignKey(d => d.AssignedSalesAgentId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_WA_Hnd_Agent");

            entity.HasOne(d => d.Conversation).WithMany(p => p.WhatsAppHandoffs)
                .HasForeignKey(d => d.ConversationId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_demorecruitment_WA_Hnd_Cnv");
        });

        modelBuilder.Entity<ConversationNote>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_WA_Note");

            entity.ToTable("ConversationNotes", "demorecruitment");

            entity.HasIndex(e => new { e.ConversationId, e.CreatedAt }, "IX_demorecruitment_WA_Note_Cnv").IsDescending(false, true);

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.Body).HasMaxLength(1000);
            entity.Property(e => e.CreatedAt).HasPrecision(0).HasDefaultValueSql("(sysutcdatetime())");

            entity.HasOne(d => d.Conversation).WithMany(p => p.ConversationNotes)
                .HasForeignKey(d => d.ConversationId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_WA_Note_Cnv");

            entity.HasOne(d => d.Author).WithMany()
                .HasForeignKey(d => d.AuthorId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_WA_Note_Author");
        });

        modelBuilder.Entity<ConversationFollowUp>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_WA_FU");

            entity.ToTable("ConversationFollowUps", "demorecruitment");

            entity.HasIndex(e => new { e.AssignedToId, e.Status, e.DueAt }, "IX_demorecruitment_WA_FU_Assignee");
            entity.HasIndex(e => e.ConversationId, "IX_demorecruitment_WA_FU_Cnv");
            entity.HasIndex(e => e.DueAt, "IX_demorecruitment_WA_FU_Due");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.Status).HasDefaultValue((byte)FollowUpStatus.Pending);
            entity.Property(e => e.Notes).HasMaxLength(500);
            entity.Property(e => e.DueAt).HasPrecision(0);
            entity.Property(e => e.CompletedAt).HasPrecision(0);
            entity.Property(e => e.CreatedAt).HasPrecision(0).HasDefaultValueSql("(sysutcdatetime())");

            entity.HasOne(d => d.Conversation).WithMany(p => p.ConversationFollowUps)
                .HasForeignKey(d => d.ConversationId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_WA_FU_Cnv");

            entity.HasOne(d => d.AssignedTo).WithMany()
                .HasForeignKey(d => d.AssignedToId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_WA_FU_Assignee");

            entity.HasOne(d => d.CreatedBy).WithMany()
                .HasForeignKey(d => d.CreatedById)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_WA_FU_CreatedBy");

            entity.HasOne(d => d.CompletedBy).WithMany()
                .HasForeignKey(d => d.CompletedById)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_demorecruitment_WA_FU_CompletedBy");
        });
    }
}
