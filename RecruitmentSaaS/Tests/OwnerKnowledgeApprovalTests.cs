using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Models.Entities;
using Xunit;

namespace RecruitmentSaaS.Tests;

public sealed class OwnerKnowledgeApprovalTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("CEO replies supplied and approved by owner in chat", true)]
    public async Task System_import_requires_explicit_approval_without_impersonating_user(string? reference, bool allowed)
    {
        await using var db = fixture.Create();
        var account = new WhatsAppAccount { Id = Guid.NewGuid(), Name = "Synthetic", DisplayPhoneNumber = "201000000000", PhoneNumberId = Guid.NewGuid().ToString(), WabaId = "test", IsActive = true };
        db.Add(account); await db.SaveChangesAsync();
        var entry = new AiSalesKnowledge { TenantId = Guid.NewGuid(), WhatsAppAccountId = account.Id,
            Kind = AiKnowledgeKind.Fact, Status = AiKnowledgeStatus.Approved, Title = "Approved company fact",
            Content = "Synthetic fact", SourceReference = "Owner confirmation", OwnerApprovalReference = reference,
            ValidFrom = DateTime.UtcNow, ValidUntil = DateTime.UtcNow.AddDays(7), ApprovedAt = DateTime.UtcNow };
        db.Add(entry);
        if (!allowed) { await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); return; }
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var stored = await db.AiSalesKnowledge.SingleAsync(k => k.Id == entry.Id);
        Assert.Null(stored.CreatedById); Assert.Null(stored.ApprovedById);
        Assert.Equal(reference, stored.OwnerApprovalReference);
    }
}
