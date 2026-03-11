using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace RecruitmentSaaS.Models.Entities;

[Table("Tenants", Schema = "platform")]
[Index("SubscriptionStatus", Name = "IX_Tenants_SubStatus")]
[Index("Subdomain", Name = "IX_Tenants_Subdomain")]
[Index("SchemaName", Name = "UQ_Tenants_Schema", IsUnique = true)]
[Index("Subdomain", Name = "UQ_Tenants_Subdomain", IsUnique = true)]
public partial class Tenant
{
    [Key]
    public Guid Id { get; set; }

    [StringLength(200)]
    public string CompanyName { get; set; } = null!;

    [StringLength(100)]
    public string SchemaName { get; set; } = null!;

    [StringLength(100)]
    public string Subdomain { get; set; } = null!;

    public byte SubscriptionStatus { get; set; }

    [Precision(0)]
    public DateTime SubscriptionEndDate { get; set; }

    public long StorageUsedBytes { get; set; }

    public long StorageQuotaBytes { get; set; }

    public bool IsActive { get; set; }

    public Guid CreatedBySuperAdminId { get; set; }

    [Precision(0)]
    public DateTime CreatedAt { get; set; }

    [Precision(0)]
    public DateTime? UpdatedAt { get; set; }

    [ForeignKey("CreatedBySuperAdminId")]
    [InverseProperty("Tenants")]
    public virtual SuperAdminUser CreatedBySuperAdmin { get; set; } = null!;
}
