using System;

namespace RecruitmentSaaS.Models.Entities;

/// <summary>
/// The Business Integration System User access token obtained from exchanging an
/// Embedded Signup authorization code. Meta grants this a single token per app that
/// covers every WABA connected through Embedded Signup (expiry: never, per Meta) —
/// so this is a single evolving row, not one row per WhatsAppAccount.
/// Never returned through any API response; read only by server-side Meta calls.
/// </summary>
public partial class MetaSystemCredential
{
    public Guid Id { get; set; }

    public string AccessToken { get; set; } = null!;

    public DateTime ObtainedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }
}
