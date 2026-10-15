using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Models.Entities;

namespace RecruitmentSaaS.Data;

public partial class RecruitmentCrmContext
{
    public DbSet<AiSalesKnowledge> AiSalesKnowledge => Set<AiSalesKnowledge>();
    public DbSet<AiCustomerMemory> AiCustomerMemories => Set<AiCustomerMemory>();
    public DbSet<AiSalesDraft> AiSalesDrafts => Set<AiSalesDraft>();

    private static void ConfigureAiSalesEngine(ModelBuilder b)
    {
        b.Entity<AiSalesKnowledge>(e =>
        {
            e.ToTable("AiSalesKnowledge", "demorecruitment", t =>
            {
                t.HasCheckConstraint("CK_AiKnowledge_Validity", "\"ValidUntil\" > \"ValidFrom\"");
                t.HasCheckConstraint("CK_AiKnowledge_Approval", "\"Status\" <> 1 OR (\"ApprovedAt\" IS NOT NULL AND (\"ApprovedById\" IS NOT NULL OR coalesce(length(trim(\"OwnerApprovalReference\"::text)) > 0, false)))");
            });
            e.HasKey(x => x.Id); e.Property(x => x.Version).IsConcurrencyToken();
            e.Property(x => x.Title).HasMaxLength(150); e.Property(x => x.Content).HasMaxLength(4000);
            e.Property(x => x.SourceReference).HasMaxLength(500);
            e.Property(x => x.OwnerApprovalReference).HasMaxLength(500);
            e.HasIndex(x => new { x.TenantId, x.WhatsAppAccountId, x.Status, x.ValidUntil });
            e.HasOne<WhatsAppAccount>().WithMany().HasForeignKey(x => x.WhatsAppAccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ApprovedById).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<AiCustomerMemory>(e =>
        {
            e.ToTable("AiCustomerMemories", "demorecruitment"); e.HasKey(x => x.ConversationId);
            e.Property(x => x.Version).IsConcurrencyToken(); e.Property(x => x.ReviewedSummary).HasMaxLength(2000);
            e.Property(x => x.ReviewedDetailsJson).HasColumnType("jsonb").HasDefaultValue("{}");
            e.HasOne<WhatsAppConversation>().WithOne().HasForeignKey<AiCustomerMemory>(x => x.ConversationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ReviewedById).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<AiSalesDraft>(e =>
        {
            e.ToTable("AiSalesDrafts", "demorecruitment"); e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.RequestKey }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.CreatedAt });
            e.HasIndex(x => new { x.ConversationId, x.CreatedAt });
            e.Property(x => x.RequestKey).HasMaxLength(128); e.Property(x => x.ContextFingerprint).HasMaxLength(64);
            e.Property(x => x.Reply).HasMaxLength(3000); e.Property(x => x.ReviewNote).HasMaxLength(500);
            e.Property(x => x.Intent).HasMaxLength(40); e.Property(x => x.Model).HasMaxLength(100); e.Property(x => x.ErrorCode).HasMaxLength(100);
            e.Property(x => x.PromptVersion).HasMaxLength(100);
            e.HasOne<WhatsAppConversation>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AiSalesAgent>().WithMany().HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.RequestedById).OnDelete(DeleteBehavior.Restrict);
        });
    }
}

