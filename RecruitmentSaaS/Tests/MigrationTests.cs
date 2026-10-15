using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RecruitmentSaaS.Migrations;
using RecruitmentSaaS.Models.Entities;
using Xunit;

namespace RecruitmentSaaS.Tests;

public sealed class MigrationTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task Foundation_upgrade_and_empty_rollback_preserve_human_data()
    {
        await using var db = fixture.Create();
        var branch = new Branch { Id = Guid.NewGuid(), Name = "Migration rehearsal", IsActive = true };
        var user = new User { Id = Guid.NewGuid(), Branch = branch, FullName = "Existing salesperson", Email = "migration@example.test", PasswordHash = "synthetic", IsActive = true, Role = 3 };
        var lead = new Lead { Id = Guid.NewGuid(), Branch = branch, RegisteredBy = user, AssignedSales = user, FullName = "Existing lead", Phone = "201234567890", Status = 1, Age = 46 };
        db.Add(lead); await db.SaveChangesAsync();
        var migration = new AiSalesFoundation { ActiveProvider = db.Database.ProviderName! };
        var engine = new AiSalesDraftEngine { ActiveProvider = db.Database.ProviderName! };
        var voice = new AiSalesVoice { ActiveProvider = db.Database.ProviderName! };
        var office = new AiOfficeBookings { ActiveProvider = db.Database.ProviderName! };
        var dispatch = new AiReviewedDispatch { ActiveProvider = db.Database.ProviderName! };
        var chatFacts = new AiChatFactReview { ActiveProvider = db.Database.ProviderName! };
        var structuredMemory = new AiStructuredCustomerMemory { ActiveProvider = db.Database.ProviderName! };
        var ownerApproval = new AiOwnerRecordedKnowledgeApproval { ActiveProvider = db.Database.ProviderName! };
        var scopedAssignment = new AiConversationScopedAssignments { ActiveProvider = db.Database.ProviderName! };
        var sql = db.GetService<IMigrationsSqlGenerator>();
        // This fixture is a separately created disposable database, never an existing installation.
        foreach (var command in sql.Generate(scopedAssignment.DownOperations)) await db.Database.ExecuteSqlRawAsync(command.CommandText);
        foreach (var command in sql.Generate(ownerApproval.DownOperations)) await db.Database.ExecuteSqlRawAsync(command.CommandText);
        foreach (var command in sql.Generate(structuredMemory.DownOperations)) await db.Database.ExecuteSqlRawAsync(command.CommandText);
        foreach (var command in sql.Generate(chatFacts.DownOperations)) await db.Database.ExecuteSqlRawAsync(command.CommandText);
        foreach (var command in sql.Generate(dispatch.DownOperations)) await db.Database.ExecuteSqlRawAsync(command.CommandText);
        foreach (var command in sql.Generate(office.DownOperations)) await db.Database.ExecuteSqlRawAsync(command.CommandText);
        foreach (var command in sql.Generate(voice.DownOperations)) await db.Database.ExecuteSqlRawAsync(command.CommandText);
        foreach (var command in sql.Generate(engine.DownOperations)) await db.Database.ExecuteSqlRawAsync(command.CommandText);
        foreach (var command in sql.Generate(migration.DownOperations)) await db.Database.ExecuteSqlRawAsync(command.CommandText);
        foreach (var command in sql.Generate(migration.UpOperations)) await db.Database.ExecuteSqlRawAsync(command.CommandText);
        foreach (var command in sql.Generate(engine.UpOperations)) await db.Database.ExecuteSqlRawAsync(command.CommandText);
        foreach (var command in sql.Generate(voice.UpOperations)) await db.Database.ExecuteSqlRawAsync(command.CommandText);
        foreach (var command in sql.Generate(office.UpOperations)) await db.Database.ExecuteSqlRawAsync(command.CommandText);
        foreach (var command in sql.Generate(dispatch.UpOperations)) await db.Database.ExecuteSqlRawAsync(command.CommandText);
        foreach (var command in sql.Generate(chatFacts.UpOperations)) await db.Database.ExecuteSqlRawAsync(command.CommandText);
        // ExecuteSqlRaw treats braces as formatting placeholders, including JSONB's literal empty object.
        foreach (var command in sql.Generate(structuredMemory.UpOperations)) await db.Database.ExecuteSqlRawAsync(command.CommandText.Replace("{", "{{").Replace("}", "}}"));
        foreach (var command in sql.Generate(ownerApproval.UpOperations)) await db.Database.ExecuteSqlRawAsync(command.CommandText);
        foreach (var command in sql.Generate(scopedAssignment.UpOperations)) await db.Database.ExecuteSqlRawAsync(command.CommandText);
        db.ChangeTracker.Clear();
        var preserved = await db.Leads.SingleAsync(l => l.Id == lead.Id);
        Assert.Equal(user.Id, preserved.AssignedSalesId); Assert.Equal((byte)46, preserved.Age);
        Assert.Null(preserved.AssignedAiSalesAgentId);
        Assert.Empty(await db.AiSalesAgents.ToListAsync());
        Assert.Empty(await db.DurableWorkItems.ToListAsync());
        Assert.Empty(await db.AiSalesDrafts.ToListAsync());
        Assert.Empty(await db.AiSalesKnowledge.ToListAsync());
    }
}
