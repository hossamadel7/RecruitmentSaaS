namespace RecruitmentSaaS.Models.Entities;

// Kept out of the scaffolded User.cs so a re-scaffold doesn't drop it.
public partial class User
{
    /// <summary>The name customers see in Arabic (e.g. the WhatsApp welcome message). Falls back to FullName.</summary>
    public string? FullNameAr { get; set; }

    public string DisplayNameAr => string.IsNullOrWhiteSpace(FullNameAr) ? FullName : FullNameAr;
}
