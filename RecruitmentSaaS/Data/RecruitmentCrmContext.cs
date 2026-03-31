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

    public virtual DbSet<ContractUpload> ContractUploads { get; set; }

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

    public virtual DbSet<SalesGoogleSheet> SalesGoogleSheets { get; set; }

    public virtual DbSet<SalesGoogleSheetUser> SalesGoogleSheetUsers { get; set; }

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

            entity.ToTable("AuditLogs", "demorecruitment");

            entity.HasIndex(e => e.CreatedAt, "IX_demorecruitment_Aud_Dt").IsDescending();

            entity.HasIndex(e => new { e.EntityId, e.CreatedAt }, "IX_demorecruitment_Aud_En").IsDescending(false, true);

            entity.HasIndex(e => new { e.EventType, e.CreatedAt }, "IX_demorecruitment_Aud_Ev").IsDescending(false, true);

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.EntityType).HasMaxLength(100);
            entity.Property(e => e.EventType).HasMaxLength(100);
            entity.Property(e => e.Ipaddress)
                .HasMaxLength(50)
                .HasColumnName("IPAddress");
        });

        modelBuilder.Entity<Branch>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_Branches");

            entity.ToTable("Branches", "demorecruitment");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.Address).HasMaxLength(500);
            entity.Property(e => e.City).HasMaxLength(100);
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.Name).HasMaxLength(200);
        });

        modelBuilder.Entity<Campaign>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_Ca");

            entity.ToTable("Campaigns", "demorecruitment");

            entity.HasIndex(e => e.Status, "IX_demorecruitment_Ca_St");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.BudgetEgp)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("BudgetEGP");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.FacebookAdId).HasMaxLength(100);
            entity.Property(e => e.FacebookAdSetId).HasMaxLength(100);
            entity.Property(e => e.FacebookCampaignId).HasMaxLength(100);
            entity.Property(e => e.Name).HasMaxLength(200);
            entity.Property(e => e.SpendEgp)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("SpendEGP");
            entity.Property(e => e.Status).HasDefaultValue((byte)1);
            entity.Property(e => e.UpdatedAt).HasPrecision(0);
            entity.Property(e => e.UtmCampaign).HasMaxLength(200);
            entity.Property(e => e.UtmContent).HasMaxLength(200);
            entity.Property(e => e.UtmMedium).HasMaxLength(100);
            entity.Property(e => e.UtmSource).HasMaxLength(100);
            entity.Property(e => e.UtmTerm).HasMaxLength(200);

            entity.HasOne(d => d.CreatedBy).WithMany(p => p.Campaigns)
                .HasForeignKey(d => d.CreatedById)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_Ca_By");
        });

        modelBuilder.Entity<Candidate>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_Cnd");

            entity.ToTable("Candidates", "demorecruitment");

            entity.HasIndex(e => e.Phone, "IX_demorecruitment_Cnd_Ph");

            entity.HasIndex(e => e.IsProfileComplete, "IX_demorecruitment_Cnd_Prf");

            entity.HasIndex(e => e.AssignedSalesId, "IX_demorecruitment_Cnd_Sa");

            entity.HasIndex(e => e.PassportNumber, "UIX_demo_Cand_Passport")
                .IsUnique()
                .HasFilter("([PassportNumber] IS NOT NULL)");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.City).HasMaxLength(100);
            entity.Property(e => e.CompletedAt).HasPrecision(0);
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.FlightDate).HasPrecision(0);
            entity.Property(e => e.FullName).HasMaxLength(200);
            entity.Property(e => e.NationalId).HasMaxLength(50);
            entity.Property(e => e.PassportNumber).HasMaxLength(50);
            entity.Property(e => e.Phone).HasMaxLength(30);
            entity.Property(e => e.Status).HasDefaultValue((byte)1);
            entity.Property(e => e.TotalPaidEgp)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("TotalPaidEGP");
            entity.Property(e => e.UpdatedAt).HasPrecision(0);
            entity.Property(e => e.VisaNumber).HasMaxLength(100);

            entity.HasOne(d => d.AssignedSales).WithMany(p => p.CandidateAssignedSales)
                .HasForeignKey(d => d.AssignedSalesId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_Cnd_Sa");

            entity.HasOne(d => d.Branch).WithMany(p => p.Candidates)
                .HasForeignKey(d => d.BranchId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_Cnd_Br");

            entity.HasOne(d => d.Company).WithMany(p => p.Candidates)
                .HasForeignKey(d => d.CompanyId)
                .HasConstraintName("FK_demorecruitment_Candidates_Company");

            entity.HasOne(d => d.CurrentPackageStage).WithMany(p => p.Candidates)
                .HasForeignKey(d => d.CurrentPackageStageId)
                .HasConstraintName("FK_demo_Cand_Stage");

            entity.HasOne(d => d.JobPackage).WithMany(p => p.Candidates)
                .HasForeignKey(d => d.JobPackageId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_Cnd_JP");

            entity.HasOne(d => d.RegisteredBy).WithMany(p => p.CandidateRegisteredBies)
                .HasForeignKey(d => d.RegisteredById)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_Cnd_Rg");
        });

        modelBuilder.Entity<CandidateActivity>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demo_CandidateActivities");

            entity.ToTable("CandidateActivities", "demorecruitment");

            entity.HasIndex(e => new { e.CandidateId, e.CreatedAt }, "IX_demo_CA_CandId").IsDescending(false, true);

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.CreatedByName).HasMaxLength(200);
            entity.Property(e => e.Description).HasMaxLength(1000);

            entity.HasOne(d => d.Candidate).WithMany(p => p.CandidateActivities)
                .HasForeignKey(d => d.CandidateId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demo_CA_Cand");

            entity.HasOne(d => d.CreatedBy).WithMany(p => p.CandidateActivities)
                .HasForeignKey(d => d.CreatedById)
                .HasConstraintName("FK_demo_CA_CreBy");
        });

        modelBuilder.Entity<CandidateStageHistory>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_CSH");

            entity.ToTable("CandidateStageHistory", "demorecruitment");

            entity.HasIndex(e => new { e.CandidateId, e.CreatedAt }, "IX_demorecruitment_CSH_Cnd").IsDescending(false, true);

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.Notes).HasMaxLength(500);
            entity.Property(e => e.OverrideReason).HasMaxLength(500);

            entity.HasOne(d => d.Candidate).WithMany(p => p.CandidateStageHistories)
                .HasForeignKey(d => d.CandidateId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_CSH_Cnd");

            entity.HasOne(d => d.ChangedBy).WithMany(p => p.CandidateStageHistories)
                .HasForeignKey(d => d.ChangedById)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_CSH_By");

            entity.HasOne(d => d.FromStageNavigation).WithMany(p => p.CandidateStageHistoryFromStageNavigations)
                .HasForeignKey(d => d.FromStageId)
                .HasConstraintName("FK_demo_CSH_FromStage");

            entity.HasOne(d => d.ToStageNavigation).WithMany(p => p.CandidateStageHistoryToStageNavigations)
                .HasForeignKey(d => d.ToStageId)
                .HasConstraintName("FK_demo_CSH_ToStage");
        });

        modelBuilder.Entity<Commission>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_Com");

            entity.ToTable("Commissions", "demorecruitment");

            entity.HasIndex(e => new { e.SalesUserId, e.CommissionMonth }, "IX_demorecruitment_Com_Mo");

            entity.HasIndex(e => e.Status, "IX_demorecruitment_Com_St");

            entity.HasIndex(e => e.CandidateId, "UQ_demorecruitment_Com_Cnd").IsUnique();

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.AmountEgp)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("AmountEGP");
            entity.Property(e => e.ApprovedAt).HasPrecision(0);
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.PaidAt).HasPrecision(0);
            entity.Property(e => e.ReversedReason).HasMaxLength(500);
            entity.Property(e => e.Status).HasDefaultValue((byte)1);

            entity.HasOne(d => d.ApprovedBy).WithMany(p => p.CommissionApprovedBies)
                .HasForeignKey(d => d.ApprovedById)
                .HasConstraintName("FK_demorecruitment_Com_ApBy");

            entity.HasOne(d => d.Candidate).WithOne(p => p.Commission)
                .HasForeignKey<Commission>(d => d.CandidateId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_Com_Cnd");

            entity.HasOne(d => d.PaidBy).WithMany(p => p.CommissionPaidBies)
                .HasForeignKey(d => d.PaidById)
                .HasConstraintName("FK_demorecruitment_Com_PdBy");

            entity.HasOne(d => d.SalesUser).WithMany(p => p.CommissionSalesUsers)
                .HasForeignKey(d => d.SalesUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_Com_Sa");
        });

        modelBuilder.Entity<CommissionSetting>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Commissi__3214EC071B98EE42");

            entity.ToTable("CommissionSettings", "demorecruitment");

            entity.Property(e => e.Id).HasDefaultValueSql("(newid())");
            entity.Property(e => e.ResetDayOfMonth).HasDefaultValue((byte)1);
        });

        modelBuilder.Entity<CommissionTier>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_CommissionTiers");

            entity.ToTable("CommissionTiers", "demorecruitment");

            entity.HasIndex(e => new { e.IsActive, e.MinDeals }, "IX_demorecruitment_CT_Active");

            entity.Property(e => e.Id).HasDefaultValueSql("(newid())");
            entity.Property(e => e.AmountPerDeal).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.UpdatedAt).HasPrecision(0);

            entity.HasOne(d => d.CreatedBy).WithMany(p => p.CommissionTiers)
                .HasForeignKey(d => d.CreatedById)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_CT_CreatedBy");
        });

        modelBuilder.Entity<Company>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_Companies");

            entity.ToTable("Companies", "demorecruitment");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.City).HasMaxLength(100);
            entity.Property(e => e.ContactEmail).HasMaxLength(255);
            entity.Property(e => e.ContactPhone).HasMaxLength(50);
            entity.Property(e => e.Country).HasMaxLength(100);
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.Name).HasMaxLength(200);
            entity.Property(e => e.UpdatedAt).HasPrecision(0);

            entity.HasOne(d => d.CreatedBy).WithMany(p => p.Companies)
                .HasForeignKey(d => d.CreatedById)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_Companies_User");
        });

        modelBuilder.Entity<CompanyJob>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_CompanyJobs");

            entity.ToTable("CompanyJobs", "demorecruitment");

            entity.HasIndex(e => e.CompanyId, "IX_demorecruitment_CompanyJobs_CompanyId");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.JobTitle).HasMaxLength(200);
            entity.Property(e => e.Notes).HasMaxLength(500);
            entity.Property(e => e.RequestedCount).HasDefaultValue(1);

            entity.HasOne(d => d.Company).WithMany(p => p.CompanyJobs)
                .HasForeignKey(d => d.CompanyId)
                .HasConstraintName("FK_demorecruitment_CompanyJobs_Company");
        });

        modelBuilder.Entity<ContractUpload>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_ContractUploads");

            entity.ToTable("ContractUploads", "demorecruitment");

            entity.HasIndex(e => e.MatchedCandidateId, "IX_demorecruitment_ContractUploads_Candidate").HasFilter("([MatchedCandidateId] IS NOT NULL)");

            entity.HasIndex(e => e.MatchStatus, "IX_demorecruitment_ContractUploads_Status");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.ExtractedEmployerName).HasMaxLength(200);
            entity.Property(e => e.ExtractedPassportNo).HasMaxLength(50);
            entity.Property(e => e.ExtractedTransactionNo).HasMaxLength(100);
            entity.Property(e => e.FileKey).HasMaxLength(500);
            entity.Property(e => e.FileName).HasMaxLength(255);

            entity.HasOne(d => d.MatchedCandidate).WithMany(p => p.ContractUploads)
                .HasForeignKey(d => d.MatchedCandidateId)
                .HasConstraintName("FK_demorecruitment_ContractUploads_Candidate");

            entity.HasOne(d => d.UploadedBy).WithMany(p => p.ContractUploads)
                .HasForeignKey(d => d.UploadedById)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_ContractUploads_User");
        });

        modelBuilder.Entity<Document>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_Doc");

            entity.ToTable("Documents", "demorecruitment");

            entity.HasIndex(e => e.CandidateId, "IX_demorecruitment_Doc_Cnd");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.FileName).HasMaxLength(255);
            entity.Property(e => e.MimeType).HasMaxLength(100);
            entity.Property(e => e.S3key)
                .HasMaxLength(500)
                .HasColumnName("S3Key");
            entity.Property(e => e.UploadedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())");

            entity.HasOne(d => d.Candidate).WithMany(p => p.Documents)
                .HasForeignKey(d => d.CandidateId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_Doc_Cnd");

            entity.HasOne(d => d.UploadedBy).WithMany(p => p.Documents)
                .HasForeignKey(d => d.UploadedById)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_Doc_By");
        });

        modelBuilder.Entity<FollowUpReminder>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_FUR");

            entity.ToTable("FollowUpReminders", "demorecruitment");

            entity.HasIndex(e => new { e.AssignedToId, e.Status, e.ReminderDate }, "IX_demorecruitment_FUR_At");

            entity.HasIndex(e => new { e.ReminderDate, e.Status }, "IX_demorecruitment_FUR_Dt");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.DismissedAt).HasPrecision(0);
            entity.Property(e => e.Notes).HasMaxLength(500);
            entity.Property(e => e.Status).HasDefaultValue((byte)1);
            entity.Property(e => e.UpdatedAt).HasPrecision(0);

            entity.HasOne(d => d.AssignedTo).WithMany(p => p.FollowUpReminderAssignedTos)
                .HasForeignKey(d => d.AssignedToId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_FUR_At");

            entity.HasOne(d => d.Candidate).WithMany(p => p.FollowUpReminders)
                .HasForeignKey(d => d.CandidateId)
                .HasConstraintName("FK_FollowUpReminders_Candidates");

            entity.HasOne(d => d.CreatedBy).WithMany(p => p.FollowUpReminderCreatedBies)
                .HasForeignKey(d => d.CreatedById)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_FUR_By");

            entity.HasOne(d => d.Lead).WithMany(p => p.FollowUpReminders)
                .HasForeignKey(d => d.LeadId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_FUR_Ld");
        });

        modelBuilder.Entity<JobPackage>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_JP");

            entity.ToTable("JobPackages", "demorecruitment");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.DestinationCountry).HasMaxLength(100);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.JobTitle).HasMaxLength(200);
            entity.Property(e => e.Name).HasMaxLength(200);
            entity.Property(e => e.PriceEgp)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("PriceEGP");
        });

        modelBuilder.Entity<Lead>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_Ld");

            entity.ToTable("Leads", "demorecruitment");

            entity.HasIndex(e => e.GoogleSheetId, "IX_Leads_GoogleSheetId").HasFilter("([GoogleSheetId] IS NOT NULL)");

            entity.HasIndex(e => e.CampaignId, "IX_demorecruitment_Ld_Ca");

            entity.HasIndex(e => e.AssignedOfficeSalesId, "IX_demorecruitment_Ld_OfSa");

            entity.HasIndex(e => e.Phone, "IX_demorecruitment_Ld_Ph");

            entity.HasIndex(e => e.AssignedSalesId, "IX_demorecruitment_Ld_Sa");

            entity.HasIndex(e => e.LeadSource, "IX_demorecruitment_Ld_Src");

            entity.HasIndex(e => e.Status, "IX_demorecruitment_Ld_St");

            entity.HasIndex(e => e.LeadCode, "UQ_demorecruitment_Ld_Code").IsUnique();

            entity.HasIndex(e => e.Phone, "UQ_demorecruitment_Ld_Ph").IsUnique();

            entity.HasIndex(e => e.FacebookLeadId, "UX_Leads_FacebookLeadId")
                .IsUnique()
                .HasFilter("([FacebookLeadId] IS NOT NULL)");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.AppointmentDate).HasPrecision(0);
            entity.Property(e => e.ConvertedAt).HasPrecision(0);
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.FacebookFormId).HasMaxLength(100);
            entity.Property(e => e.FacebookLeadId).HasMaxLength(100);
            entity.Property(e => e.FullName).HasMaxLength(200);
            entity.Property(e => e.InterestedCountry).HasMaxLength(100);
            entity.Property(e => e.InterestedJobTitle).HasMaxLength(200);
            entity.Property(e => e.LastContactedAt).HasPrecision(0);
            entity.Property(e => e.LeadCode)
                .HasMaxLength(8)
                .HasComputedColumnSql("('LD-'+right('00000'+CONVERT([nvarchar](10),[LeadSequence]),(5)))", true);
            entity.Property(e => e.LeadSequence).ValueGeneratedOnAdd();
            entity.Property(e => e.Phone).HasMaxLength(30);
            entity.Property(e => e.ReferredByName).HasMaxLength(200);
            entity.Property(e => e.ReferredByPhone).HasMaxLength(30);
            entity.Property(e => e.Status).HasDefaultValue((byte)1);
            entity.Property(e => e.UpdatedAt).HasPrecision(0);
            entity.Property(e => e.UtmCampaign).HasMaxLength(200);
            entity.Property(e => e.UtmContent).HasMaxLength(200);
            entity.Property(e => e.UtmMedium).HasMaxLength(100);
            entity.Property(e => e.UtmSource).HasMaxLength(100);

            entity.HasOne(d => d.AssignedOfficeSales).WithMany(p => p.LeadAssignedOfficeSales)
                .HasForeignKey(d => d.AssignedOfficeSalesId)
                .HasConstraintName("FK__Leads__AssignedO__2E70E1FD");

            entity.HasOne(d => d.AssignedSales).WithMany(p => p.LeadAssignedSales)
                .HasForeignKey(d => d.AssignedSalesId)
                .HasConstraintName("FK_demorecruitment_Ld_Sa");

            entity.HasOne(d => d.Branch).WithMany(p => p.Leads)
                .HasForeignKey(d => d.BranchId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_Ld_Br");

            entity.HasOne(d => d.Campaign).WithMany(p => p.Leads)
                .HasForeignKey(d => d.CampaignId)
                .HasConstraintName("FK_demorecruitment_Ld_Ca");

            entity.HasOne(d => d.GoogleSheet).WithMany(p => p.Leads)
                .HasForeignKey(d => d.GoogleSheetId)
                .HasConstraintName("FK_Leads_GoogleSheet");

            entity.HasOne(d => d.RegisteredBy).WithMany(p => p.LeadRegisteredBies)
                .HasForeignKey(d => d.RegisteredById)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_Ld_Rg");
        });

        modelBuilder.Entity<LeadActivity>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_LA");

            entity.ToTable("LeadActivities", "demorecruitment");

            entity.HasIndex(e => e.NextFollowUpDate, "IX_demorecruitment_LA_Fup").HasFilter("([NextFollowUpDate] IS NOT NULL)");

            entity.HasIndex(e => new { e.LeadId, e.CreatedAt }, "IX_demorecruitment_LA_Ld").IsDescending(false, true);

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.ActorType).HasDefaultValue((byte)1);
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.CreatedByName).HasMaxLength(200);
            entity.Property(e => e.Description).HasMaxLength(1000);
            entity.Property(e => e.EntityType).HasMaxLength(50);

            entity.HasOne(d => d.CreatedBy).WithMany(p => p.LeadActivities)
                .HasForeignKey(d => d.CreatedById)
                .HasConstraintName("FK_demorecruitment_LA_By");

            entity.HasOne(d => d.Lead).WithMany(p => p.LeadActivities)
                .HasForeignKey(d => d.LeadId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_LA_Ld");
        });

        modelBuilder.Entity<LeadCallLog>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_LCL");

            entity.ToTable("LeadCallLog", "demorecruitment", tb => tb.HasTrigger("trg_LCL_WriteActivity"));

            entity.HasIndex(e => new { e.LeadId, e.CalledAt }, "IX_demorecruitment_LCL_Ld").IsDescending(false, true);

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.CalledAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.Note).HasMaxLength(500);

            entity.HasOne(d => d.CalledBy).WithMany(p => p.LeadCallLogs)
                .HasForeignKey(d => d.CalledById)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_LCL_By");

            entity.HasOne(d => d.Lead).WithMany(p => p.LeadCallLogs)
                .HasForeignKey(d => d.LeadId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_LCL_Ld");
        });

        modelBuilder.Entity<LeadFunnelHistory>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_LFH");

            entity.ToTable("LeadFunnelHistory", "demorecruitment");

            entity.HasIndex(e => new { e.LeadId, e.CreatedAt }, "IX_demorecruitment_LFH_Ld").IsDescending(false, true);

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.Note).HasMaxLength(500);

            entity.HasOne(d => d.ChangedBy).WithMany(p => p.LeadFunnelHistories)
                .HasForeignKey(d => d.ChangedById)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_LFH_By");

            entity.HasOne(d => d.Lead).WithMany(p => p.LeadFunnelHistories)
                .HasForeignKey(d => d.LeadId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_LFH_Ld");
        });

        modelBuilder.Entity<LeadVisit>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_LV");

            entity.ToTable("LeadVisits", "demorecruitment", tb => tb.HasTrigger("trg_LV_WriteActivity"));

            entity.HasIndex(e => new { e.BranchId, e.VisitDateTime }, "IX_demorecruitment_LV_Br").IsDescending(false, true);

            entity.HasIndex(e => new { e.LeadId, e.CreatedAt }, "IX_demorecruitment_LV_Ld").IsDescending(false, true);

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.Notes).HasMaxLength(1000);
            entity.Property(e => e.VisitDateTime)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())");

            entity.HasOne(d => d.AssignedSalesUser).WithMany(p => p.LeadVisitAssignedSalesUsers)
                .HasForeignKey(d => d.AssignedSalesUserId)
                .HasConstraintName("FK_demorecruitment_LV_Sa");

            entity.HasOne(d => d.Branch).WithMany(p => p.LeadVisits)
                .HasForeignKey(d => d.BranchId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_LV_Br");

            entity.HasOne(d => d.JobPackage).WithMany(p => p.LeadVisits)
                .HasForeignKey(d => d.JobPackageId)
                .HasConstraintName("FK_demorecruitment_LV_JP");

            entity.HasOne(d => d.Lead).WithMany(p => p.LeadVisits)
                .HasForeignKey(d => d.LeadId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_LV_Ld");

            entity.HasOne(d => d.ReceptionUser).WithMany(p => p.LeadVisitReceptionUsers)
                .HasForeignKey(d => d.ReceptionUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_LV_Re");
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_Not");

            entity.ToTable("Notifications", "demorecruitment");

            entity.HasIndex(e => new { e.UserId, e.IsRead, e.CreatedAt }, "IX_demorecruitment_Not_Us").IsDescending(false, false, true);

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.Body).HasMaxLength(500);
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.Link).HasMaxLength(500);
            entity.Property(e => e.ReadAt).HasColumnType("datetime");
            entity.Property(e => e.Title).HasMaxLength(200);

            entity.HasOne(d => d.User).WithMany(p => p.Notifications)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_Not_Us");
        });

        modelBuilder.Entity<PackageStage>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demo_PackageStages");

            entity.ToTable("PackageStages", "demorecruitment");

            entity.HasIndex(e => new { e.PackageId, e.StageOrder }, "IX_demo_PS_PackageId");

            entity.HasIndex(e => new { e.PackageId, e.StageOrder }, "UQ_demo_PS_Order").IsUnique();

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.RequiredMinPaymentEgp)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("RequiredMinPaymentEGP");
            entity.Property(e => e.StageName).HasMaxLength(200);

            entity.HasOne(d => d.Package).WithMany(p => p.PackageStages)
                .HasForeignKey(d => d.PackageId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demo_PS_Package");

            entity.HasOne(d => d.StageType).WithMany(p => p.PackageStages)
                .HasForeignKey(d => d.StageTypeId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_DR_PS_StageType");
        });

        modelBuilder.Entity<PassportDownloadLog>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_PDL");

            entity.ToTable("PassportDownloadLogs", "demorecruitment");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.DownloadedAt)
                .HasDefaultValueSql("(getutcdate())")
                .HasColumnType("datetime");

            entity.HasOne(d => d.DownloadedBy).WithMany(p => p.PassportDownloadLogs)
                .HasForeignKey(d => d.DownloadedById)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PDL_User");

            entity.HasOne(d => d.Package).WithMany(p => p.PassportDownloadLogs)
                .HasForeignKey(d => d.PackageId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PDL_Package");
        });

        modelBuilder.Entity<PassportDownloadedCandidate>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_PDC");

            entity.ToTable("PassportDownloadedCandidates", "demorecruitment");

            entity.HasIndex(e => e.CandidateId, "UQ_PDC_Candidate").IsUnique();

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.DownloadedAt)
                .HasDefaultValueSql("(getutcdate())")
                .HasColumnType("datetime");

            entity.HasOne(d => d.Candidate).WithOne(p => p.PassportDownloadedCandidate)
                .HasForeignKey<PassportDownloadedCandidate>(d => d.CandidateId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PDC_Cand");

            entity.HasOne(d => d.Log).WithMany(p => p.PassportDownloadedCandidates)
                .HasForeignKey(d => d.LogId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PDC_Log");
        });

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_Pay");

            entity.ToTable("Payments", "demorecruitment");

            entity.HasIndex(e => e.CandidateId, "IX_demorecruitment_Pay_Cnd");

            entity.HasIndex(e => e.PaymentDate, "IX_demorecruitment_Pay_Dt");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.AmountEgp)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("AmountEGP");
            entity.Property(e => e.ApprovedAt).HasColumnType("datetime");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.Notes).HasMaxLength(500);
            entity.Property(e => e.RejectionReason).HasMaxLength(500);
            entity.Property(e => e.Status).HasDefaultValue((byte)1);

            entity.HasOne(d => d.ApprovedBy).WithMany(p => p.PaymentApprovedBies)
                .HasForeignKey(d => d.ApprovedById)
                .HasConstraintName("FK_demorecruitment_Pay_ApprovedBy");

            entity.HasOne(d => d.Candidate).WithMany(p => p.Payments)
                .HasForeignKey(d => d.CandidateId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_Pay_Cnd");

            entity.HasOne(d => d.RecordedBy).WithMany(p => p.PaymentRecordedBies)
                .HasForeignKey(d => d.RecordedById)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_Pay_By");
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_RT");

            entity.ToTable("RefreshTokens", "demorecruitment");

            entity.HasIndex(e => e.Token, "UQ_demorecruitment_RT_Tok").IsUnique();

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.ExpiresAt).HasPrecision(0);
            entity.Property(e => e.Token).HasMaxLength(500);

            entity.HasOne(d => d.User).WithMany(p => p.RefreshTokens)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_RT_Us");
        });

        modelBuilder.Entity<Refund>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_Ref");

            entity.ToTable("Refunds", "demorecruitment");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.AmountEgp)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("AmountEGP");
            entity.Property(e => e.ExecutedAt).HasPrecision(0);
            entity.Property(e => e.Reason).HasMaxLength(500);
            entity.Property(e => e.RejectReason).HasMaxLength(500);
            entity.Property(e => e.RequestedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.ReviewedAt).HasPrecision(0);
            entity.Property(e => e.Status).HasDefaultValue((byte)1);

            entity.HasOne(d => d.Candidate).WithMany(p => p.Refunds)
                .HasForeignKey(d => d.CandidateId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_Ref_Cnd");

            entity.HasOne(d => d.ExecutedBy).WithMany(p => p.RefundExecutedBies)
                .HasForeignKey(d => d.ExecutedById)
                .HasConstraintName("FK_demorecruitment_Ref_ExBy");

            entity.HasOne(d => d.RefundPayment).WithMany(p => p.Refunds)
                .HasForeignKey(d => d.RefundPaymentId)
                .HasConstraintName("FK_demorecruitment_Ref_Pay");

            entity.HasOne(d => d.RequestedBy).WithMany(p => p.RefundRequestedBies)
                .HasForeignKey(d => d.RequestedById)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_Ref_RqBy");

            entity.HasOne(d => d.ReviewedBy).WithMany(p => p.RefundReviewedBies)
                .HasForeignKey(d => d.ReviewedById)
                .HasConstraintName("FK_demorecruitment_Ref_RvBy");
        });

        modelBuilder.Entity<SalaryPayment>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_SalaryPayments");

            entity.ToTable("SalaryPayments", "demorecruitment");

            entity.HasIndex(e => e.SalaryMonth, "IX_demorecruitment_SP_Month").IsDescending();

            entity.HasIndex(e => e.Status, "IX_demorecruitment_SP_Status");

            entity.HasIndex(e => e.UserId, "IX_demorecruitment_SP_UserId");

            entity.HasIndex(e => new { e.UserId, e.SalaryMonth }, "UQ_demorecruitment_SP_UserMonth").IsUnique();

            entity.Property(e => e.Id).HasDefaultValueSql("(newid())");
            entity.Property(e => e.Adjustment).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.AdjustmentNote).HasMaxLength(500);
            entity.Property(e => e.ApprovedAt).HasPrecision(0);
            entity.Property(e => e.BaseSalary).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.PaidAt).HasPrecision(0);
            entity.Property(e => e.Status).HasDefaultValue((byte)1);
            entity.Property(e => e.TotalAmount)
                .HasComputedColumnSql("([BaseSalary]+[Adjustment])", true)
                .HasColumnType("decimal(19, 2)");
            entity.Property(e => e.UpdatedAt).HasPrecision(0);

            entity.HasOne(d => d.ApprovedBy).WithMany(p => p.SalaryPaymentApprovedBies)
                .HasForeignKey(d => d.ApprovedById)
                .HasConstraintName("FK_demorecruitment_SP_ApprovedBy");

            entity.HasOne(d => d.CreatedBy).WithMany(p => p.SalaryPaymentCreatedBies)
                .HasForeignKey(d => d.CreatedById)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_SP_CreatedBy");

            entity.HasOne(d => d.PaidBy).WithMany(p => p.SalaryPaymentPaidBies)
                .HasForeignKey(d => d.PaidById)
                .HasConstraintName("FK_demorecruitment_SP_PaidBy");

            entity.HasOne(d => d.User).WithMany(p => p.SalaryPaymentUsers)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_SP_User");
        });

        modelBuilder.Entity<SalesGoogleSheet>(entity =>
        {
            entity.ToTable("SalesGoogleSheets", "demorecruitment");

            entity.Property(e => e.Id).HasDefaultValueSql("(newid())");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.LastImportedRow).HasDefaultValue(1);
            entity.Property(e => e.Name).HasMaxLength(200);
            entity.Property(e => e.SheetName)
                .HasMaxLength(200)
                .HasDefaultValue("Sheet1");
            entity.Property(e => e.SpreadsheetId).HasMaxLength(200);

            entity.HasOne(d => d.Campaign).WithMany(p => p.SalesGoogleSheets)
                .HasForeignKey(d => d.CampaignId)
                .HasConstraintName("FK_SalesGoogleSheets_Campaign");

            entity.HasOne(d => d.CreatedBy).WithMany(p => p.SalesGoogleSheets)
                .HasForeignKey(d => d.CreatedById)
                .HasConstraintName("FK_SalesGoogleSheets_CreatedBy");
        });

        modelBuilder.Entity<SalesGoogleSheetUser>(entity =>
        {
            entity.ToTable("SalesGoogleSheetUsers", "demorecruitment");

            entity.HasIndex(e => new { e.SheetId, e.SalesUserId }, "UQ_SheetUsers_SheetUser").IsUnique();

            entity.Property(e => e.Id).HasDefaultValueSql("(newid())");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.IsActive).HasDefaultValue(true);

            entity.HasOne(d => d.SalesUser).WithMany(p => p.SalesGoogleSheetUsers)
                .HasForeignKey(d => d.SalesUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SheetUsers_User");

            entity.HasOne(d => d.Sheet).WithMany(p => p.SalesGoogleSheetUsers)
                .HasForeignKey(d => d.SheetId)
                .HasConstraintName("FK_SheetUsers_Sheet");
        });

        modelBuilder.Entity<StageActionCompletion>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_SAC");

            entity.ToTable("StageActionCompletions", "demorecruitment");

            entity.HasIndex(e => new { e.CandidateId, e.PackageStageId }, "UQ_SAC_CandStage").IsUnique();

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.CompletedAt)
                .HasDefaultValueSql("(getutcdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.Notes).HasMaxLength(500);

            entity.HasOne(d => d.Candidate).WithMany(p => p.StageActionCompletions)
                .HasForeignKey(d => d.CandidateId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SAC_Candidate");

            entity.HasOne(d => d.CompletedBy).WithMany(p => p.StageActionCompletions)
                .HasForeignKey(d => d.CompletedById)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SAC_User");

            entity.HasOne(d => d.PackageStage).WithMany(p => p.StageActionCompletions)
                .HasForeignKey(d => d.PackageStageId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SAC_Stage");
        });

        modelBuilder.Entity<StageApprovalRequest>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_SAR");

            entity.ToTable("StageApprovalRequests", "demorecruitment");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.AdminNote).HasMaxLength(500);
            entity.Property(e => e.AmountPaid).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.MinPaymentRequired).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.RequestNote).HasMaxLength(500);
            entity.Property(e => e.RequestedAt)
                .HasDefaultValueSql("(getutcdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.ReviewedAt).HasColumnType("datetime");
            entity.Property(e => e.Status).HasDefaultValue((byte)1);

            entity.HasOne(d => d.Candidate).WithMany(p => p.StageApprovalRequests)
                .HasForeignKey(d => d.CandidateId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SAR_Candidate");

            entity.HasOne(d => d.FromStage).WithMany(p => p.StageApprovalRequestFromStages)
                .HasForeignKey(d => d.FromStageId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SAR_FromStage");

            entity.HasOne(d => d.RequestedBy).WithMany(p => p.StageApprovalRequestRequestedBies)
                .HasForeignKey(d => d.RequestedById)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SAR_ReqBy");

            entity.HasOne(d => d.ReviewedBy).WithMany(p => p.StageApprovalRequestReviewedBies)
                .HasForeignKey(d => d.ReviewedById)
                .HasConstraintName("FK_SAR_RevBy");

            entity.HasOne(d => d.ToStage).WithMany(p => p.StageApprovalRequestToStages)
                .HasForeignKey(d => d.ToStageId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SAR_ToStage");
        });

        modelBuilder.Entity<StageType>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_DR_ST");

            entity.ToTable("StageTypes", "demorecruitment");

            entity.HasIndex(e => e.StageCode, "UQ_DR_ST_Code").IsUnique();

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.ActionFields).HasMaxLength(500);
            entity.Property(e => e.ActionLabel).HasMaxLength(200);
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getutcdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.Name).HasMaxLength(200);
            entity.Property(e => e.StageCode).HasMaxLength(50);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_Users");

            entity.ToTable("Users", "demorecruitment");

            entity.HasIndex(e => e.BranchId, "IX_demorecruitment_Us_Br");

            entity.HasIndex(e => e.Email, "UQ_demorecruitment_Us_Email").IsUnique();

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.BaseSalary).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.Email).HasMaxLength(255);
            entity.Property(e => e.FullName).HasMaxLength(200);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.LastLoginAt).HasPrecision(0);
            entity.Property(e => e.PasswordHash).HasMaxLength(500);

            entity.HasOne(d => d.Branch).WithMany(p => p.Users)
                .HasForeignKey(d => d.BranchId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_Us_Br");

            entity.HasOne(d => d.Manager).WithMany(p => p.InverseManager).HasConstraintName("FK_demorecruitment_Us_Manager");
        });

        modelBuilder.Entity<VisaUpload>(entity =>
        {
            entity.ToTable("VisaUploads", "demorecruitment");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.ExtractedFullName).HasMaxLength(200);
            entity.Property(e => e.ExtractedPassportNo).HasMaxLength(100);
            entity.Property(e => e.ExtractedVisaNo).HasMaxLength(100);
            entity.Property(e => e.FileKey).HasMaxLength(500);
            entity.Property(e => e.FileName).HasMaxLength(500);
            entity.Property(e => e.Notes).HasMaxLength(500);
            entity.Property(e => e.UploadedAt)
                .HasDefaultValueSql("(getutcdate())")
                .HasColumnType("datetime");

            entity.HasOne(d => d.MatchedCandidate).WithMany(p => p.VisaUploads)
                .HasForeignKey(d => d.MatchedCandidateId)
                .HasConstraintName("FK_VisaUploads_Candidate");

            entity.HasOne(d => d.UploadedBy).WithMany(p => p.VisaUploads)
                .HasForeignKey(d => d.UploadedById)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_VisaUploads_User");
        });

        modelBuilder.Entity<VwCampaignPerformance>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("vw_CampaignPerformance", "demorecruitment");
        });

        modelBuilder.Entity<VwDailyLead>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("vw_DailyLeads", "demorecruitment");
        });

        modelBuilder.Entity<VwDailyPayment>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("vw_DailyPayments", "demorecruitment");

            entity.Property(e => e.TotalEgp)
                .HasColumnType("decimal(38, 2)")
                .HasColumnName("TotalEGP");
        });

        modelBuilder.Entity<VwLeadFunnelSummary>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("vw_LeadFunnelSummary", "demorecruitment");
        });

        modelBuilder.Entity<VwSalesPerformance>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("vw_SalesPerformance", "demorecruitment");

            entity.Property(e => e.RevenueEgp)
                .HasColumnType("decimal(38, 2)")
                .HasColumnName("RevenueEGP");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
