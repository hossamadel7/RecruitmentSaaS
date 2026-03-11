using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace RecruitmentSaaS.Models.Entities;

[Table("RefreshTokens", Schema = "demorecruitment")]
[Index("Token", Name = "UQ_demorecruitment_RT_Tok", IsUnique = true)]
public partial class RefreshToken
{
    [Key]
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    [StringLength(500)]
    public string Token { get; set; } = null!;

    [Precision(0)]
    public DateTime ExpiresAt { get; set; }

    public bool IsRevoked { get; set; }

    [Precision(0)]
    public DateTime CreatedAt { get; set; }

    [ForeignKey("UserId")]
    [InverseProperty("RefreshTokens")]
    public virtual User User { get; set; } = null!;
}
