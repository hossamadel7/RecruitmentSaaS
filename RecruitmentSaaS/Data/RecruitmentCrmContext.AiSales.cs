using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Models.Entities;

namespace RecruitmentSaaS.Data;

public partial class RecruitmentCrmContext
{
    public DbSet<AiSalesAgent> AiSalesAgents => Set<AiSalesAgent>();
    public DbSet<AiLeadAssignment> AiLeadAssignments => Set<AiLeadAssignment>();
    public DbSet<ConversationControl> ConversationControls => Set<ConversationControl>();
    public DbSet<AiControlAudit> AiControlAudits => Set<AiControlAudit>();
    public DbSet<AiControlRequest> AiControlRequests => Set<AiControlRequest>();
    public DbSet<DurableWorkItem> DurableWorkItems => Set<DurableWorkItem>();
    public DbSet<AiOutboundIntent> AiOutboundIntents => Set<AiOutboundIntent>();

    private static void ConfigureAiSales(ModelBuilder b)
    {
        b.Entity<AiSalesAgent>(e =>
        {
            e.ToTable("AiSalesAgents", "demorecruitment", t =>
            {
                t.HasCheckConstraint("CK_AiAgent_Mode", "\"Mode\" IN (0,1)");
                t.HasCheckConstraint("CK_AiAgent_Limit", "\"MaxActiveConversations\" BETWEEN 1 AND 1000");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.DisplayName).HasMaxLength(100);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasOne<WhatsAppAccount>().WithMany().HasForeignKey(x => x.WhatsAppAccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.TeamManagerId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TenantId, x.WhatsAppAccountId });
        });
        b.Entity<Lead>(e =>
        {
            e.ToTable("Leads", "demorecruitment", t => t.HasCheckConstraint("CK_Lead_SalesOwner", "\"AssignedSalesId\" IS NULL OR \"AssignedAiSalesAgentId\" IS NULL"));
            e.HasOne<AiSalesAgent>().WithMany().HasForeignKey(x => x.AssignedAiSalesAgentId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.AssignedSalesId).IsConcurrencyToken();
            e.Property(x => x.AssignedAiSalesAgentId).IsConcurrencyToken();
        });
        b.Entity<WhatsAppConversation>(e =>
        {
            e.ToTable("WhatsAppConversations", "demorecruitment", t => t.HasCheckConstraint("CK_Conversation_SalesOwner", "\"AssignedSalesAgentId\" IS NULL OR \"AssignedAiSalesAgentId\" IS NULL"));
            e.HasOne<AiSalesAgent>().WithMany().HasForeignKey(x => x.AssignedAiSalesAgentId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.AssignedSalesAgentId).IsConcurrencyToken();
            e.Property(x => x.AssignedAiSalesAgentId).IsConcurrencyToken();
        });
        b.Entity<AiLeadAssignment>(e =>
        {
            e.ToTable("AiLeadAssignments", "demorecruitment", t => t.HasCheckConstraint("CK_AiAssignment_Actor", "\"AssignedById\" IS NOT NULL OR coalesce(length(trim(\"SourceReference\"::text)) > 0, false)")); e.HasKey(x => x.Id);
            e.HasIndex(x => x.LeadId).IsUnique().HasFilter("\"EndedAt\" IS NULL AND NOT \"ConversationOnly\"");
            e.HasIndex(x => x.ConversationId).IsUnique().HasFilter("\"EndedAt\" IS NULL");
            e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.SourceReference).HasMaxLength(500);
            e.HasOne<Lead>().WithMany().HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<WhatsAppConversation>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AiSalesAgent>().WithMany().HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.AssignedById).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ConversationControl>(e =>
        {
            e.ToTable("ConversationControls", "demorecruitment"); e.HasKey(x => x.ConversationId);
            e.Property(x => x.OwnershipVersion).IsConcurrencyToken();
            e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.HandoffSummary).HasMaxLength(2000);
            e.HasOne<WhatsAppConversation>().WithOne().HasForeignKey<ConversationControl>(x => x.ConversationId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<AiControlAudit>(e =>
        {
            e.ToTable("AiControlAudits", "demorecruitment"); e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.ConversationId, x.CreatedAt });
            e.Property(x => x.Event).HasMaxLength(60); e.Property(x => x.Reason).HasMaxLength(500);
            e.HasOne<WhatsAppConversation>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<WhatsAppMessage>(e =>
        {
            e.HasOne<AiSalesAgent>().WithMany().HasForeignKey(x => x.SenderAiSalesAgentId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable("WhatsAppMessages", "demorecruitment", t => t.HasCheckConstraint("CK_Message_SalesAuthor", "\"SenderUserId\" IS NULL OR \"SenderAiSalesAgentId\" IS NULL"));
        });
        b.Entity<AiControlRequest>(e =>
        {
            e.ToTable("AiControlRequests", "demorecruitment"); e.HasKey(x => new { x.TenantId, x.Key });
            e.Property(x => x.Key).HasMaxLength(128); e.Property(x => x.Fingerprint).HasMaxLength(64);
        });
        b.Entity<DurableWorkItem>(e =>
        {
            e.ToTable("DurableWorkItems", "demorecruitment"); e.HasKey(x => x.Id);
            e.HasIndex(x => x.DeduplicationKey).IsUnique();
            e.HasIndex(x => x.AvailableAt).HasFilter("\"CompletedAt\" IS NULL");
            e.Property(x => x.Kind).HasMaxLength(30); e.Property(x => x.DeduplicationKey).HasMaxLength(160);
            e.Property(x => x.ErrorCode).HasMaxLength(100);
        });
        b.Entity<AiOutboundIntent>(e =>
        {
            e.ToTable("AiOutboundIntents", "demorecruitment"); e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.OperationKey }).IsUnique();
            e.Property(x => x.OperationKey).HasMaxLength(128); e.Property(x => x.State).HasMaxLength(30);
            e.Property(x => x.ErrorCode).HasMaxLength(100);
            e.HasIndex(x => x.DraftId).IsUnique().HasFilter("\"DraftId\" IS NOT NULL");
            e.HasOne<AiSalesDraft>().WithMany().HasForeignKey(x => x.DraftId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.RequestedById).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<WhatsAppMessage>().WithMany().HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<WhatsAppConversation>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
