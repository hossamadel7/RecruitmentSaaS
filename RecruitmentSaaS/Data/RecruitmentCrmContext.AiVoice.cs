using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Models.Entities;

namespace RecruitmentSaaS.Data;

public partial class RecruitmentCrmContext
{
    public DbSet<AiVoiceTurn> AiVoiceTurns => Set<AiVoiceTurn>();
    public DbSet<AiVoicePart> AiVoiceParts => Set<AiVoicePart>();
    private static void ConfigureAiVoice(ModelBuilder b)
    {
        b.Entity<AiVoiceTurn>(e =>
        {
            e.ToTable("AiVoiceTurns", "demorecruitment"); e.HasKey(x => x.Id);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.Property(x => x.Transcript).HasMaxLength(4000); e.Property(x => x.ReviewedText).HasMaxLength(4000); e.Property(x => x.ErrorCode).HasMaxLength(100);
            e.HasIndex(x => new { x.TenantId, x.Status, x.AvailableAt });
            e.HasIndex(x => new { x.ConversationId, x.CreatedAt });
            e.HasOne<WhatsAppConversation>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AiSalesAgent>().WithMany().HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.RequestedById).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ReviewedById).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<AiVoicePart>(e =>
        {
            e.ToTable("AiVoiceParts", "demorecruitment"); e.HasKey(x => x.MessageId);
            e.Property(x => x.MediaId).HasMaxLength(100); e.Property(x => x.MediaSha256).HasMaxLength(100); e.Property(x => x.ContentType).HasMaxLength(100);
            e.Property(x => x.Transcript).HasMaxLength(2000); e.Property(x => x.Model).HasMaxLength(100); e.Property(x => x.ErrorCode).HasMaxLength(100);
            e.HasIndex(x => x.LastAttemptAt);
            e.HasOne<WhatsAppMessage>().WithOne().HasForeignKey<AiVoicePart>(x => x.MessageId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AiVoiceTurn>().WithMany().HasForeignKey(x => x.TurnId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
