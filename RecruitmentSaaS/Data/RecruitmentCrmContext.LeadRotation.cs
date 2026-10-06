using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Models.Entities;

namespace RecruitmentSaaS.Data;

// Weighted lead rotation: who receives leads, in which order, and how many per turn.
public partial class RecruitmentCrmContext
{
    public virtual DbSet<LeadRotationMember> LeadRotationMembers { get; set; } = null!;

    public virtual DbSet<LeadRotationState> LeadRotationStates { get; set; } = null!;

    public virtual DbSet<FormVisit> FormVisits { get; set; } = null!;

    private static void ConfigureLeadRotation(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LeadRotationMember>(entity =>
        {
            entity.HasKey(e => new { e.ScopeKey, e.UserId }).HasName("PK_demorecruitment_LRM");

            entity.ToTable("LeadRotationMembers", "demorecruitment");

            entity.Property(e => e.ScopeKey).HasMaxLength(60);
            // No DB default on Share: EF would skip an explicit 0 (paused) and let the default win

            entity.HasOne(d => d.User).WithMany()
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_demorecruitment_LRM_User");
        });

        // Chats left without a salesperson when one is deactivated, waiting with their team
        modelBuilder.Entity<WhatsAppConversation>(entity =>
        {
            entity.HasIndex(e => e.PendingTeamManagerId, "IX_demorecruitment_WA_Cnv_PendTeam")
                .HasFilter("(\"PendingTeamManagerId\" IS NOT NULL)");

            entity.HasOne<User>().WithMany()
                .HasForeignKey(e => e.PendingTeamManagerId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_demorecruitment_WA_Cnv_PendTeam");
        });

        // AI WhatsApp assistant
        modelBuilder.Entity<WhatsAppConversation>(entity =>
        {
            entity.Property(e => e.IntakeName).HasMaxLength(200);
            entity.Property(e => e.IntakeJob).HasMaxLength(200);
            entity.Property(e => e.IntakeHandoffReason).HasMaxLength(200);
            entity.Property(e => e.IntakeStartedAt).HasPrecision(0);
            entity.Property(e => e.IntakeAgeAt).HasPrecision(0);
            entity.Property(e => e.IntakeLastCustomerAt).HasPrecision(0);
            entity.HasIndex(e => e.IntakeStatus, "IX_demorecruitment_WA_Cnv_Intake").HasFilter("(\"IntakeStatus\" = 1)");
        });
        modelBuilder.Entity<LeadFormSetting>(entity =>
        {
            entity.Property(e => e.AiTestNumbers).HasMaxLength(1000);
            entity.Property(e => e.AiGreeting).HasMaxLength(1000);
        });

        modelBuilder.Entity<FormVisit>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_FormVisits");
            entity.ToTable("FormVisits", "demorecruitment");
            entity.HasIndex(e => e.CreatedAt, "IX_demorecruitment_FormVisits_Created");
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.CreatedAt).HasPrecision(0);
            entity.Property(e => e.LastSeenAt).HasPrecision(0);
            entity.Property(e => e.SubmittedAt).HasPrecision(0);
            entity.Property(e => e.Page).HasMaxLength(20);
            entity.Property(e => e.TeamSlug).HasMaxLength(50);
            entity.Property(e => e.Source).HasMaxLength(20);
            entity.Property(e => e.UtmCampaign).HasMaxLength(100);
            entity.Property(e => e.Device).HasMaxLength(20);
            entity.Property(e => e.FieldsTouched).HasMaxLength(100);
            entity.Property(e => e.LastField).HasMaxLength(20);
            entity.Property(e => e.LastError).HasMaxLength(200);
        });

        modelBuilder.Entity<LeadRotationState>(entity =>
        {
            entity.HasKey(e => e.ScopeKey).HasName("PK_demorecruitment_LRS");

            entity.ToTable("LeadRotationStates", "demorecruitment");

            entity.Property(e => e.ScopeKey).HasMaxLength(60);
            entity.Property(e => e.UpdatedAt).HasPrecision(0);
        });
    }
}
