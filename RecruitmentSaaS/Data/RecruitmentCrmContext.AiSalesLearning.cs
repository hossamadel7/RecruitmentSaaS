using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Models.Entities;

namespace RecruitmentSaaS.Data;
public partial class RecruitmentCrmContext
{
    public DbSet<AiLearningAccount> AiLearningAccounts => Set<AiLearningAccount>();
    public DbSet<AiConversationAnalysis> AiConversationAnalyses => Set<AiConversationAnalysis>();
    public DbSet<AiKnowledgeIndex> AiKnowledgeIndexes => Set<AiKnowledgeIndex>();
    public DbSet<AiSalesFeedback> AiSalesFeedback => Set<AiSalesFeedback>();
    public DbSet<AiLearningUsage> AiLearningUsage => Set<AiLearningUsage>();
    public DbSet<AiEvaluationCase> AiEvaluationCases => Set<AiEvaluationCase>();
    public DbSet<AiEvaluationResult> AiEvaluationResults => Set<AiEvaluationResult>();
    private static void ConfigureAiSalesLearning(ModelBuilder b)
    {
        b.Entity<AiLearningAccount>(e => {
            e.ToTable("AiLearningAccounts", "demorecruitment"); e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.WhatsAppAccountId }).IsUnique(); e.Property(x => x.Version).IsConcurrencyToken();
            e.HasOne<WhatsAppAccount>().WithMany().HasForeignKey(x => x.WhatsAppAccountId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<AiConversationAnalysis>(e => {
            e.ToTable("AiConversationAnalyses", "demorecruitment"); e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.WhatsAppAccountId, x.ConversationId, x.Fingerprint }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.State, x.AvailableAt }); e.Property(x => x.Version).IsConcurrencyToken();
            e.Property(x => x.Fingerprint).HasMaxLength(64); e.Property(x => x.ErrorCode).HasMaxLength(100);
            e.Property(x => x.InferredStage).HasMaxLength(40); e.Property(x => x.Language).HasMaxLength(20);
            e.Property(x => x.VerifiedOutcome).HasMaxLength(40); e.Property(x => x.OutcomeEvidence).HasMaxLength(200);
            e.Property(x => x.Cohort).HasMaxLength(300); e.Property(x => x.InsightsJson).HasColumnType("jsonb");
            e.HasOne<WhatsAppConversation>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<WhatsAppAccount>().WithMany().HasForeignKey(x => x.WhatsAppAccountId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.SalespersonId).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<AiKnowledgeIndex>(e => {
            e.ToTable("AiKnowledgeIndexes", "demorecruitment"); e.HasKey(x => x.KnowledgeId);
            e.HasIndex(x => new { x.TenantId, x.WhatsAppAccountId, x.ContentHash });
            e.Property(x => x.ContentHash).HasMaxLength(64); e.Property(x => x.SourceFingerprint).HasMaxLength(64);
            e.Property(x => x.EmbeddingModel).HasMaxLength(100); e.Property(x => x.Embedding).HasColumnType("real[]");
            e.HasOne<AiSalesKnowledge>().WithOne().HasForeignKey<AiKnowledgeIndex>(x => x.KnowledgeId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<AiConversationAnalysis>().WithMany().HasForeignKey(x => x.AnalysisId).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<AiSalesFeedback>(e => {
            e.ToTable("AiSalesFeedback", "demorecruitment"); e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.DraftId, x.CreatedAt });
            e.Property(x => x.Note).HasMaxLength(1000); e.Property(x => x.EditedReply).HasMaxLength(3000);
            e.HasOne<AiSalesDraft>().WithMany().HasForeignKey(x => x.DraftId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<AiLearningUsage>(e => {
            e.ToTable("AiLearningUsage", "demorecruitment"); e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.CreatedAt });
            e.Property(x => x.Kind).HasMaxLength(30); e.Property(x => x.Model).HasMaxLength(100); e.Property(x => x.Status).HasMaxLength(100);
        });
        b.Entity<AiEvaluationCase>(e => {
            e.ToTable("AiEvaluationCases", "demorecruitment"); e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.WhatsAppAccountId, x.Dataset });
            e.Property(x => x.Dataset).HasMaxLength(100); e.Property(x => x.Scenario).HasMaxLength(100);
            e.Property(x => x.Expected).HasMaxLength(2000); e.Property(x => x.ContextJson).HasColumnType("jsonb");
            e.HasOne<WhatsAppAccount>().WithMany().HasForeignKey(x => x.WhatsAppAccountId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<AiConversationAnalysis>().WithMany().HasForeignKey(x => x.AnalysisId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<AiEvaluationResult>(e => {
            e.ToTable("AiEvaluationResults", "demorecruitment"); e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.RunId }); e.Property(x => x.ReviewJson).HasColumnType("jsonb");
            e.Property(x => x.Configuration).HasMaxLength(100); e.Property(x => x.Baseline).HasMaxLength(100);
            e.Property(x => x.Reply).HasMaxLength(3000);
            e.HasOne<AiEvaluationCase>().WithMany().HasForeignKey(x => x.CaseId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
