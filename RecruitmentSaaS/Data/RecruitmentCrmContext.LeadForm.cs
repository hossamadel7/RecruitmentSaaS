using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Models.Entities;

namespace RecruitmentSaaS.Data;

// Public registration form additions: applicant age + admin-editable form settings.
public partial class RecruitmentCrmContext
{
    public virtual DbSet<LeadFormSetting> LeadFormSettings { get; set; } = null!;

    public virtual DbSet<TeamLeadForm> TeamLeadForms { get; set; } = null!;

    private static void ConfigureLeadForm(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Lead>(entity =>
        {
            entity.Property(e => e.Age).HasColumnType("smallint");

            entity.HasIndex(e => e.TeamManagerId, "IX_demorecruitment_Ld_TeamMgr")
                .HasFilter("(\"TeamManagerId\" IS NOT NULL)");

            entity.HasOne(d => d.TeamManager).WithMany()
                .HasForeignKey(d => d.TeamManagerId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_Ld_TeamMgr");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.Property(e => e.FullNameAr).HasMaxLength(200);
            entity.Ignore(e => e.DisplayNameAr);
        });

        modelBuilder.Entity<LeadFormSetting>(entity =>
        {
            entity.Property(e => e.WelcomeMessageEnabled).HasDefaultValue(false);
            entity.Property(e => e.WelcomeMessage).HasMaxLength(4000);
            entity.Property(e => e.AutoFollowupEnabledAt).HasPrecision(0);
        });

        modelBuilder.Entity<TeamLeadForm>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_TLF");

            entity.ToTable("TeamLeadForms", "demorecruitment");

            entity.HasIndex(e => e.ManagerId, "UQ_demorecruitment_TLF_Mgr").IsUnique();
            entity.HasIndex(e => e.Slug, "UQ_demorecruitment_TLF_Slug").IsUnique();

            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Slug).HasMaxLength(50);
            entity.Property(e => e.WhatsAppNumber).HasMaxLength(30);
            entity.Property(e => e.CreatedAt).HasPrecision(0).HasDefaultValueSql("(clock_timestamp() AT TIME ZONE 'utc')");
            entity.Property(e => e.UpdatedAt).HasPrecision(0);

            entity.HasOne(d => d.Manager).WithMany()
                .HasForeignKey(d => d.ManagerId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_demorecruitment_TLF_Mgr");
        });

        modelBuilder.Entity<LeadFormSetting>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_demorecruitment_LFS");

            entity.ToTable("LeadFormSettings", "demorecruitment");

            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.SeniorAgeThreshold).HasDefaultValue((byte)45);
            entity.Property(e => e.SalesWhatsAppNumber).HasMaxLength(30);
            entity.Property(e => e.SalesWhatsAppMessage).HasMaxLength(1000);
            entity.Property(e => e.UpdatedAt).HasPrecision(0);
        });
    }
}
