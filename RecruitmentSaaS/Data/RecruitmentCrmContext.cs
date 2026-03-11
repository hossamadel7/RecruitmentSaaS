using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Models.Entities;

namespace RecruitmentCrmContext;

public partial class RecruitmentCrmContext : DbContext
{
    public RecruitmentCrmContext(DbContextOptions<RecruitmentCrmContext> options)
        : base(options)
    {
    }

    public virtual DbSet<AuditLog> AuditLogs { get; set; }

    public virtual DbSet<Branch> Branches { get; set; }

    public virtual DbSet<Campaign> Campaigns { get; set; }

    public virtual DbSet<Candidate> Candidates { get; set; }

    public virtual DbSet<CandidateStageHistory> CandidateStageHistories { get; set; }

    public virtual DbSet<Commission> Commissions { get; set; }

    public virtual DbSet<Document> Documents { get; set; }

    public virtual DbSet<FollowUpReminder> FollowUpReminders { get; set; }

    public virtual DbSet<JobPackage> JobPackages { get; set; }

    public virtual DbSet<Lead> Leads { get; set; }

    public virtual DbSet<LeadActivity> LeadActivities { get; set; }

    public virtual DbSet<LeadCallLog> LeadCallLogs { get; set; }

    public virtual DbSet<LeadFunnelHistory> LeadFunnelHistories { get; set; }

    public virtual DbSet<LeadVisit> LeadVisits { get; set; }

    public virtual DbSet<Notification> Notifications { get; set; }

    public virtual DbSet<Payment> Payments { get; set; }

    public virtual DbSet<RefreshToken> RefreshTokens { get; set; }

    public virtual DbSet<Refund> Refunds { get; set; }

    public virtual DbSet<SuperAdminUser> SuperAdminUsers { get; set; }

    public virtual DbSet<Tenant> Tenants { get; set; }

    public virtual DbSet<User> Users { get; set; }

    public virtual DbSet<VwCampaignPerformance> VwCampaignPerformances { get; set; }

    public virtual DbSet<VwDailyLead> VwDailyLeads { get; set; }

    public virtual DbSet<VwDailyPayment> VwDailyPayments { get; set; }

    public virtual DbSet<VwLeadFunnelSummary> VwLeadFunnelSummaries { get; set; }

    public virtual DbSet<VwSalesPerformance> VwSalesPerformances { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.UseCollation("Arabic_CI_AI");

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_Aud");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
        });

        modelBuilder.Entity<Branch>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_Branches");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
        });

        modelBuilder.Entity<Campaign>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_Ca");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.Status).HasDefaultValue((byte)1);

            entity.HasOne(d => d.CreatedBy).WithMany(p => p.Campaigns)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_Ca_By");
        });

        modelBuilder.Entity<Candidate>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_Cnd");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.CurrentStage).HasDefaultValue((byte)1);
            entity.Property(e => e.Status).HasDefaultValue((byte)1);

            entity.HasOne(d => d.AssignedSales).WithMany(p => p.CandidateAssignedSales)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_Cnd_Sa");

            entity.HasOne(d => d.Branch).WithMany(p => p.Candidates)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_Cnd_Br");

            entity.HasOne(d => d.JobPackage).WithMany(p => p.Candidates)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_Cnd_JP");

            entity.HasOne(d => d.RegisteredBy).WithMany(p => p.CandidateRegisteredBies)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_Cnd_Rg");
        });

        modelBuilder.Entity<CandidateStageHistory>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_CSH");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");

            entity.HasOne(d => d.Candidate).WithMany(p => p.CandidateStageHistories)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_CSH_Cnd");

            entity.HasOne(d => d.ChangedBy).WithMany(p => p.CandidateStageHistories)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_CSH_By");
        });

        modelBuilder.Entity<Commission>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_Com");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.Status).HasDefaultValue((byte)1);

            entity.HasOne(d => d.ApprovedBy).WithMany(p => p.CommissionApprovedBies).HasConstraintName("FK_demorecruitment_Com_ApBy");

            entity.HasOne(d => d.Candidate).WithOne(p => p.Commission)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_Com_Cnd");

            entity.HasOne(d => d.PaidBy).WithMany(p => p.CommissionPaidBies).HasConstraintName("FK_demorecruitment_Com_PdBy");

            entity.HasOne(d => d.SalesUser).WithMany(p => p.CommissionSalesUsers)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_Com_Sa");
        });

        modelBuilder.Entity<Document>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_Doc");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.UploadedAt).HasDefaultValueSql("(sysutcdatetime())");

            entity.HasOne(d => d.Candidate).WithMany(p => p.Documents)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_Doc_Cnd");

            entity.HasOne(d => d.UploadedBy).WithMany(p => p.Documents)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_Doc_By");
        });

        modelBuilder.Entity<FollowUpReminder>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_FUR");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.Status).HasDefaultValue((byte)1);

            entity.HasOne(d => d.AssignedTo).WithMany(p => p.FollowUpReminderAssignedTos)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_FUR_At");

            entity.HasOne(d => d.CreatedBy).WithMany(p => p.FollowUpReminderCreatedBies)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_FUR_By");

            entity.HasOne(d => d.Lead).WithMany(p => p.FollowUpReminders)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_FUR_Ld");
        });

        modelBuilder.Entity<JobPackage>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_JP");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
        });

        modelBuilder.Entity<Lead>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_Ld");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.LeadCode).HasComputedColumnSql("('LD-'+right('00000'+CONVERT([nvarchar](10),[LeadSequence]),(5)))", true);
            entity.Property(e => e.LeadSequence).ValueGeneratedOnAdd();
            entity.Property(e => e.Status).HasDefaultValue((byte)1);

            entity.HasOne(d => d.AssignedOfficeSales).WithMany(p => p.LeadAssignedOfficeSales).HasConstraintName("FK__Leads__AssignedO__2E70E1FD");

            entity.HasOne(d => d.AssignedSales).WithMany(p => p.LeadAssignedSales).HasConstraintName("FK_demorecruitment_Ld_Sa");

            entity.HasOne(d => d.Branch).WithMany(p => p.Leads)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_Ld_Br");

            entity.HasOne(d => d.Campaign).WithMany(p => p.Leads).HasConstraintName("FK_demorecruitment_Ld_Ca");

            entity.HasOne(d => d.RegisteredBy).WithMany(p => p.LeadRegisteredBies)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_Ld_Rg");
        });

        modelBuilder.Entity<LeadActivity>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_LA");

            entity.HasIndex(e => e.NextFollowUpDate, "IX_demorecruitment_LA_Fup").HasFilter("([NextFollowUpDate] IS NOT NULL)");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.ActorType).HasDefaultValue((byte)1);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");

            entity.HasOne(d => d.CreatedBy).WithMany(p => p.LeadActivities).HasConstraintName("FK_demorecruitment_LA_By");

            entity.HasOne(d => d.Lead).WithMany(p => p.LeadActivities)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_LA_Ld");
        });

        modelBuilder.Entity<LeadCallLog>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_LCL");

            entity.ToTable("LeadCallLog", "demorecruitment", tb => tb.HasTrigger("trg_LCL_WriteActivity"));

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.CalledAt).HasDefaultValueSql("(sysutcdatetime())");

            entity.HasOne(d => d.CalledBy).WithMany(p => p.LeadCallLogs)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_LCL_By");

            entity.HasOne(d => d.Lead).WithMany(p => p.LeadCallLogs)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_LCL_Ld");
        });

        modelBuilder.Entity<LeadFunnelHistory>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_LFH");

            entity.ToTable("LeadFunnelHistory", "demorecruitment", tb => tb.HasTrigger("trg_LFH_WriteActivity"));

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");

            entity.HasOne(d => d.ChangedBy).WithMany(p => p.LeadFunnelHistories)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_LFH_By");

            entity.HasOne(d => d.Lead).WithMany(p => p.LeadFunnelHistories)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_LFH_Ld");
        });

        modelBuilder.Entity<LeadVisit>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_LV");

            entity.ToTable("LeadVisits", "demorecruitment", tb => tb.HasTrigger("trg_LV_WriteActivity"));

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.VisitDateTime).HasDefaultValueSql("(sysutcdatetime())");

            entity.HasOne(d => d.AssignedSalesUser).WithMany(p => p.LeadVisitAssignedSalesUsers).HasConstraintName("FK_demorecruitment_LV_Sa");

            entity.HasOne(d => d.Branch).WithMany(p => p.LeadVisits)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_LV_Br");

            entity.HasOne(d => d.JobPackage).WithMany(p => p.LeadVisits).HasConstraintName("FK_demorecruitment_LV_JP");

            entity.HasOne(d => d.Lead).WithMany(p => p.LeadVisits)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_LV_Ld");

            entity.HasOne(d => d.ReceptionUser).WithMany(p => p.LeadVisitReceptionUsers)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_LV_Re");
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_Not");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");

            entity.HasOne(d => d.User).WithMany(p => p.Notifications)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_Not_Us");
        });

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_Pay");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.Status).HasDefaultValue((byte)1);

            entity.HasOne(d => d.ApprovedBy).WithMany(p => p.PaymentApprovedBies).HasConstraintName("FK_demorecruitment_Pay_ApprovedBy");

            entity.HasOne(d => d.Candidate).WithMany(p => p.Payments)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_Pay_Cnd");

            entity.HasOne(d => d.RecordedBy).WithMany(p => p.PaymentRecordedBies)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_Pay_By");
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_RT");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");

            entity.HasOne(d => d.User).WithMany(p => p.RefreshTokens)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_RT_Us");
        });

        modelBuilder.Entity<Refund>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_Ref");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.RequestedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.Status).HasDefaultValue((byte)1);

            entity.HasOne(d => d.Candidate).WithMany(p => p.Refunds)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_Ref_Cnd");

            entity.HasOne(d => d.ExecutedBy).WithMany(p => p.RefundExecutedBies).HasConstraintName("FK_demorecruitment_Ref_ExBy");

            entity.HasOne(d => d.RefundPayment).WithMany(p => p.Refunds).HasConstraintName("FK_demorecruitment_Ref_Pay");

            entity.HasOne(d => d.RequestedBy).WithMany(p => p.RefundRequestedBies)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_Ref_RqBy");

            entity.HasOne(d => d.ReviewedBy).WithMany(p => p.RefundReviewedBies).HasConstraintName("FK_demorecruitment_Ref_RvBy");
        });

        modelBuilder.Entity<SuperAdminUser>(entity =>
        {
            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
        });

        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.StorageQuotaBytes).HasDefaultValueSql("((10737418240.))");
            entity.Property(e => e.SubscriptionStatus).HasDefaultValue((byte)2);

            entity.HasOne(d => d.CreatedBySuperAdmin).WithMany(p => p.Tenants)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Tenants_SuperAdmin");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_Users");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.IsActive).HasDefaultValue(true);

            entity.HasOne(d => d.Branch).WithMany(p => p.Users)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_Us_Br");
        });

        modelBuilder.Entity<VwCampaignPerformance>(entity =>
        {
            entity.ToView("vw_CampaignPerformance", "demorecruitment");
        });

        modelBuilder.Entity<VwDailyLead>(entity =>
        {
            entity.ToView("vw_DailyLeads", "demorecruitment");
        });

        modelBuilder.Entity<VwDailyPayment>(entity =>
        {
            entity.ToView("vw_DailyPayments", "demorecruitment");
        });

        modelBuilder.Entity<VwLeadFunnelSummary>(entity =>
        {
            entity.ToView("vw_LeadFunnelSummary", "demorecruitment");
        });

        modelBuilder.Entity<VwSalesPerformance>(entity =>
        {
            entity.ToView("vw_SalesPerformance", "demorecruitment");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
