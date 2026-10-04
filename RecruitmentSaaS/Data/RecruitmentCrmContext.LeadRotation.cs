using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Models.Entities;

namespace RecruitmentSaaS.Data;

// Weighted lead rotation: who receives leads, in which order, and how many per turn.
public partial class RecruitmentCrmContext
{
    public virtual DbSet<LeadRotationMember> LeadRotationMembers { get; set; } = null!;

    public virtual DbSet<LeadRotationState> LeadRotationStates { get; set; } = null!;

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

        modelBuilder.Entity<LeadRotationState>(entity =>
        {
            entity.HasKey(e => e.ScopeKey).HasName("PK_demorecruitment_LRS");

            entity.ToTable("LeadRotationStates", "demorecruitment");

            entity.Property(e => e.ScopeKey).HasMaxLength(60);
            entity.Property(e => e.UpdatedAt).HasPrecision(0);
        });
    }
}
