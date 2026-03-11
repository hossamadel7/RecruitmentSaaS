using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace RecruitmentSaaS.Models.Entities;

[Table("SuperAdminUsers", Schema = "platform")]
[Index("Email", Name = "UQ_SA_Email", IsUnique = true)]
public partial class SuperAdminUser
{
    [Key]
    public Guid Id { get; set; }

    [StringLength(200)]
    public string FullName { get; set; } = null!;

    [StringLength(255)]
    public string Email { get; set; } = null!;

    [StringLength(500)]
    public string PasswordHash { get; set; } = null!;

    public bool IsActive { get; set; }

    [Precision(0)]
    public DateTime CreatedAt { get; set; }

    [Precision(0)]
    public DateTime? LastLoginAt { get; set; }

    [InverseProperty("CreatedBySuperAdmin")]
    public virtual ICollection<Tenant> Tenants { get; set; } = new List<Tenant>();
}
