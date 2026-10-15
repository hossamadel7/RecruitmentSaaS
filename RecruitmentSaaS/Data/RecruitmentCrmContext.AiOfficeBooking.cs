using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Models.Entities;

namespace RecruitmentSaaS.Data;
public partial class RecruitmentCrmContext
{
    public DbSet<AiOfficePolicy> AiOfficePolicies => Set<AiOfficePolicy>();
    public DbSet<AiOfficeBooking> AiOfficeBookings => Set<AiOfficeBooking>();
    public DbSet<AiOfficeBookingEvent> AiOfficeBookingEvents => Set<AiOfficeBookingEvent>();
    private static void ConfigureAiOfficeBooking(ModelBuilder b)
    {
        b.Entity<AiOfficePolicy>(e =>
        {
            e.ToTable("AiOfficePolicies", "demorecruitment"); e.HasKey(x => x.Id); e.Property(x => x.Version).IsConcurrencyToken();
            e.Property(x => x.SourceReference).HasMaxLength(500);
            e.HasIndex(x => new { x.TenantId, x.WhatsAppAccountId, x.BranchId }).IsUnique();
            e.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<WhatsAppAccount>().WithMany().HasForeignKey(x => x.WhatsAppAccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ApprovedById).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<AiOfficeBooking>(e =>
        {
            e.ToTable("AiOfficeBookings", "demorecruitment"); e.HasKey(x => x.Id); e.Property(x => x.Version).IsConcurrencyToken();
            e.Property(x => x.Purpose).HasMaxLength(500); e.Property(x => x.ConfirmationFingerprint).HasMaxLength(64);
            e.HasIndex(x => x.LeadId).IsUnique().HasFilter("\"Status\" = 0");
            e.HasIndex(x => new { x.TenantId, x.CreatedAt });
            e.HasOne<WhatsAppConversation>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Lead>().WithMany().HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AiSalesAgent>().WithMany().HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.TeamManagerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AiOfficePolicy>().WithMany().HasForeignKey(x => x.PolicyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<WhatsAppMessage>().WithMany().HasForeignKey(x => x.ConfirmationMessageId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AiVoiceTurn>().WithMany().HasForeignKey(x => x.ConfirmationVoiceTurnId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<FollowUpReminder>().WithMany().HasForeignKey(x => x.ReminderId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<LeadVisit>().WithMany().HasForeignKey(x => x.VerifiedVisitId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ReviewedById).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<AiOfficeBookingEvent>(e =>
        {
            e.ToTable("AiOfficeBookingEvents", "demorecruitment"); e.HasKey(x => x.Id);
            e.Property(x => x.Action).HasMaxLength(40); e.Property(x => x.Reason).HasMaxLength(500); e.Property(x => x.Purpose).HasMaxLength(500);
            e.Property(x => x.ConfirmationFingerprint).HasMaxLength(64);
            e.HasIndex(x => new { x.BookingId, x.Version }).IsUnique();
            e.HasOne<AiOfficeBooking>().WithMany().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<LeadFunnelHistory>().WithMany().HasForeignKey(x => x.FunnelHistoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<WhatsAppMessage>().WithMany().HasForeignKey(x => x.ConfirmationMessageId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
