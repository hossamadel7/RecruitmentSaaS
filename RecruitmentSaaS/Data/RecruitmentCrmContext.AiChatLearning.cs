using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Models.Entities;

namespace RecruitmentSaaS.Data;
public partial class RecruitmentCrmContext
{
    public DbSet<AiChatFactCandidate> AiChatFactCandidates => Set<AiChatFactCandidate>();
    private static void ConfigureAiChatLearning(ModelBuilder b)
    {
        b.Entity<AiChatFactCandidate>(e =>
        {
            e.ToTable("AiChatFactCandidates", "demorecruitment", t => t.HasCheckConstraint("CK_ChatFact_State", "\"State\" IN (0,1,2)"));
            e.HasKey(x => x.Id); e.Property(x => x.Version).IsConcurrencyToken();
            e.Property(x => x.SourceFingerprint).HasMaxLength(64); e.Property(x => x.Topic).HasMaxLength(50);
            e.Property(x => x.SourceStatement).HasMaxLength(4000); e.Property(x => x.Speaker).HasMaxLength(30);
            e.HasIndex(x => new { x.TenantId, x.WhatsAppAccountId, x.SourceMessageId, x.SourceFingerprint, x.Topic }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.WhatsAppAccountId, x.State, x.CreatedAt });
            e.HasOne<WhatsAppAccount>().WithMany().HasForeignKey(x => x.WhatsAppAccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<WhatsAppMessage>().WithMany().HasForeignKey(x => x.SourceMessageId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<AiSalesKnowledge>().WithMany().HasForeignKey(x => x.KnowledgeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ReviewedById).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
