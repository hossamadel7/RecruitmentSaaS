using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Models.Entities;
namespace RecruitmentSaaS.Data;

public partial class RecruitmentCrmContext
{
    public DbSet<AiAdminChannel> AiAdminChannels => Set<AiAdminChannel>();
    public DbSet<AiAdminMessage> AiAdminMessages => Set<AiAdminMessage>();
    public DbSet<AiAdminClarification> AiAdminClarifications => Set<AiAdminClarification>();
    private static void ConfigureAiAdminLearning(ModelBuilder b)
    {
        b.Entity<AiAdminChannel>(e => {
            e.ToTable("AiAdminChannels", "demorecruitment"); e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.WhatsAppAccountId }).IsUnique();
            e.HasIndex(x => new { x.WhatsAppAccountId, x.Phone }).IsUnique();
            e.Property(x => x.Phone).HasMaxLength(16); e.Property(x => x.VerificationHash).HasMaxLength(64);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.AdminUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<WhatsAppAccount>().WithMany().HasForeignKey(x => x.WhatsAppAccountId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<AiAdminMessage>(e => {
            e.ToTable("AiAdminMessages", "demorecruitment"); e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ChannelId, x.OperationKey }).IsUnique();
            e.HasIndex(x => x.WhatsAppMessageId).IsUnique().HasFilter("\"WhatsAppMessageId\" IS NOT NULL");
            e.Property(x => x.OperationKey).HasMaxLength(160); e.Property(x => x.WhatsAppMessageId).HasMaxLength(100);
            e.Property(x => x.Text).HasMaxLength(4000); e.Property(x => x.State).HasMaxLength(30); e.Property(x => x.ErrorCode).HasMaxLength(100);
            e.HasOne<AiAdminChannel>().WithMany().HasForeignKey(x => x.ChannelId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<AiAdminClarification>().WithMany().HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<AiAdminClarification>(e => {
            e.ToTable("AiAdminClarifications", "demorecruitment"); e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.ConversationId, x.CustomerMessageId }).IsUnique();
            e.HasIndex(x => x.Code).IsUnique(); e.HasIndex(x => new { x.TenantId, x.State, x.Deadline });
            e.Property(x => x.Code).HasMaxLength(20); e.Property(x => x.Question).HasMaxLength(2000);
            e.Property(x => x.Answer).HasMaxLength(3000); e.Property(x => x.ErrorCode).HasMaxLength(100);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasOne<AiAdminChannel>().WithMany().HasForeignKey(x => x.ChannelId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<WhatsAppConversation>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<AiSalesAgent>().WithMany().HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<WhatsAppMessage>().WithMany().HasForeignKey(x => x.CustomerMessageId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AiSalesKnowledge>().WithMany().HasForeignKey(x => x.KnowledgeId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<AiSalesDraft>().WithMany().HasForeignKey(x => x.ReplyDraftId).OnDelete(DeleteBehavior.SetNull);
        });
    }
}
