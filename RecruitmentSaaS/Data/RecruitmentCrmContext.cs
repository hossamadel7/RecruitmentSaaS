using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Models.Entities;

namespace RecruitmentSaaS.Data;

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

    public virtual DbSet<CandidateActivity> CandidateActivities { get; set; }

    public virtual DbSet<CandidateStageHistory> CandidateStageHistories { get; set; }

    public virtual DbSet<Commission> Commissions { get; set; }

    public virtual DbSet<CommissionSetting> CommissionSettings { get; set; }

    public virtual DbSet<CommissionTier> CommissionTiers { get; set; }

    public virtual DbSet<Company> Companies { get; set; }

    public virtual DbSet<CompanyJob> CompanyJobs { get; set; }

    public virtual DbSet<Document> Documents { get; set; }

    public virtual DbSet<FollowUpReminder> FollowUpReminders { get; set; }

    public virtual DbSet<JobPackage> JobPackages { get; set; }

    public virtual DbSet<Lead> Leads { get; set; }

    public virtual DbSet<LeadActivity> LeadActivities { get; set; }

    public virtual DbSet<LeadCallLog> LeadCallLogs { get; set; }

    public virtual DbSet<LeadFunnelHistory> LeadFunnelHistories { get; set; }

    public virtual DbSet<LeadVisit> LeadVisits { get; set; }

    public virtual DbSet<Notification> Notifications { get; set; }

    public virtual DbSet<PackageStage> PackageStages { get; set; }

    public virtual DbSet<PassportDownloadLog> PassportDownloadLogs { get; set; }

    public virtual DbSet<PassportDownloadedCandidate> PassportDownloadedCandidates { get; set; }

    public virtual DbSet<Payment> Payments { get; set; }

    public virtual DbSet<RefreshToken> RefreshTokens { get; set; }

    public virtual DbSet<Refund> Refunds { get; set; }

    public virtual DbSet<SalaryPayment> SalaryPayments { get; set; }

    public virtual DbSet<StageActionCompletion> StageActionCompletions { get; set; }

    public virtual DbSet<StageApprovalRequest> StageApprovalRequests { get; set; }

    public virtual DbSet<StageType> StageTypes { get; set; }

    public virtual DbSet<User> Users { get; set; }

    public virtual DbSet<VisaUpload> VisaUploads { get; set; }

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

            entity.HasIndex(e => e.PassportNumber, "UIX_demo_Cand_Passport")
                .IsUnique()
                .HasFilter("([PassportNumber] IS NOT NULL)");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.Status).HasDefaultValue((byte)1);

            entity.HasOne(d => d.AssignedSales).WithMany(p => p.CandidateAssignedSales)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_Cnd_Sa");

            entity.HasOne(d => d.Branch).WithMany(p => p.Candidates)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_Cnd_Br");

            entity.HasOne(d => d.Company).WithMany(p => p.Candidates).HasConstraintName("FK_demorecruitment_Candidates_Company");

            entity.HasOne(d => d.CurrentPackageStage).WithMany(p => p.Candidates).HasConstraintName("FK_demo_Cand_Stage");

            entity.HasOne(d => d.JobPackage).WithMany(p => p.Candidates)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_Cnd_JP");

            entity.HasOne(d => d.RegisteredBy).WithMany(p => p.CandidateRegisteredBies)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_Cnd_Rg");
        });

        modelBuilder.Entity<CandidateActivity>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demo_CandidateActivities");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");

            entity.HasOne(d => d.Candidate).WithMany(p => p.CandidateActivities)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demo_CA_Cand");

            entity.HasOne(d => d.CreatedBy).WithMany(p => p.CandidateActivities).HasConstraintName("FK_demo_CA_CreBy");
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

            entity.HasOne(d => d.FromStageNavigation).WithMany(p => p.CandidateStageHistoryFromStageNavigations).HasConstraintName("FK_demo_CSH_FromStage");

            entity.HasOne(d => d.ToStageNavigation).WithMany(p => p.CandidateStageHistoryToStageNavigations).HasConstraintName("FK_demo_CSH_ToStage");
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

        modelBuilder.Entity<CommissionSetting>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Commissi__3214EC071B98EE42");

            entity.Property(e => e.Id).HasDefaultValueSql("(newid())");
            entity.Property(e => e.ResetDayOfMonth).HasDefaultValue((byte)1);
        });

        modelBuilder.Entity<CommissionTier>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_CommissionTiers");

            entity.Property(e => e.Id).HasDefaultValueSql("(newid())");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.IsActive).HasDefaultValue(true);

            entity.HasOne(d => d.CreatedBy).WithMany(p => p.CommissionTiers)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_CT_CreatedBy");
        });

        modelBuilder.Entity<Company>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_Companies");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.IsActive).HasDefaultValue(true);

            entity.HasOne(d => d.CreatedBy).WithMany(p => p.Companies)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_Companies_User");
        });

        modelBuilder.Entity<CompanyJob>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_CompanyJobs");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.RequestedCount).HasDefaultValue(1);

            entity.HasOne(d => d.Company).WithMany(p => p.CompanyJobs).HasConstraintName("FK_demorecruitment_CompanyJobs_Company");
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

            entity.HasIndex(e => e.FacebookLeadId, "UX_Leads_FacebookLeadId")
                .IsUnique()
                .HasFilter("([FacebookLeadId] IS NOT NULL)");

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

        modelBuilder.Entity<PackageStage>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demo_PackageStages");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.IsActive).HasDefaultValue(true);

            entity.HasOne(d => d.Package).WithMany(p => p.PackageStages)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demo_PS_Package");

            entity.HasOne(d => d.StageType).WithMany(p => p.PackageStages)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_DR_PS_StageType");
        });

        modelBuilder.Entity<PassportDownloadLog>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_PDL");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.DownloadedAt).HasDefaultValueSql("(getutcdate())");

            entity.HasOne(d => d.DownloadedBy).WithMany(p => p.PassportDownloadLogs)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PDL_User");

            entity.HasOne(d => d.Package).WithMany(p => p.PassportDownloadLogs)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PDL_Package");
        });

        modelBuilder.Entity<PassportDownloadedCandidate>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_PDC");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.DownloadedAt).HasDefaultValueSql("(getutcdate())");

            entity.HasOne(d => d.Candidate).WithOne(p => p.PassportDownloadedCandidate)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PDC_Cand");

            entity.HasOne(d => d.Log).WithMany(p => p.PassportDownloadedCandidates)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PDC_Log");
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

        modelBuilder.Entity<SalaryPayment>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_SalaryPayments");

            entity.Property(e => e.Id).HasDefaultValueSql("(newid())");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.Status).HasDefaultValue((byte)1);
            entity.Property(e => e.TotalAmount).HasComputedColumnSql("([BaseSalary]+[Adjustment])", true);

            entity.HasOne(d => d.ApprovedBy).WithMany(p => p.SalaryPaymentApprovedBies).HasConstraintName("FK_demorecruitment_SP_ApprovedBy");

            entity.HasOne(d => d.CreatedBy).WithMany(p => p.SalaryPaymentCreatedBies)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_SP_CreatedBy");

            entity.HasOne(d => d.PaidBy).WithMany(p => p.SalaryPaymentPaidBies).HasConstraintName("FK_demorecruitment_SP_PaidBy");

            entity.HasOne(d => d.User).WithMany(p => p.SalaryPaymentUsers)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_SP_User");
        });

        modelBuilder.Entity<StageActionCompletion>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_SAC");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.CompletedAt).HasDefaultValueSql("(getutcdate())");

            entity.HasOne(d => d.Candidate).WithMany(p => p.StageActionCompletions)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SAC_Candidate");

            entity.HasOne(d => d.CompletedBy).WithMany(p => p.StageActionCompletions)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SAC_User");

            entity.HasOne(d => d.PackageStage).WithMany(p => p.StageActionCompletions)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SAC_Stage");
        });

        modelBuilder.Entity<StageApprovalRequest>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_SAR");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.RequestedAt).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.Status).HasDefaultValue((byte)1);

            entity.HasOne(d => d.Candidate).WithMany(p => p.StageApprovalRequests)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SAR_Candidate");

            entity.HasOne(d => d.FromStage).WithMany(p => p.StageApprovalRequestFromStages)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SAR_FromStage");

            entity.HasOne(d => d.RequestedBy).WithMany(p => p.StageApprovalRequestRequestedBies)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SAR_ReqBy");

            entity.HasOne(d => d.ReviewedBy).WithMany(p => p.StageApprovalRequestReviewedBies).HasConstraintName("FK_SAR_RevBy");

            entity.HasOne(d => d.ToStage).WithMany(p => p.StageApprovalRequestToStages)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SAR_ToStage");
        });

        modelBuilder.Entity<StageType>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_DR_ST");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
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

        modelBuilder.Entity<VisaUpload>(entity =>
        {
            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.UploadedAt).HasDefaultValueSql("(getutcdate())");

            entity.HasOne(d => d.MatchedCandidate).WithMany(p => p.VisaUploads).HasConstraintName("FK_VisaUploads_Candidate");

            entity.HasOne(d => d.UploadedBy).WithMany(p => p.VisaUploads)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_VisaUploads_User");
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
