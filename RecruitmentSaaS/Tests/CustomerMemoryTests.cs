using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RecruitmentSaaS.Migrations;
using RecruitmentSaaS.Models.Entities;
using RecruitmentSaaS.Services;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace RecruitmentSaaS.Tests;

public sealed partial class FoundationTests
{
    [Fact]
    public async Task Structured_memory_additive_upgrade_preserves_existing_reviewed_summary_and_version()
    {
        var s = await SeedAsync(); await EngineSeedAsync(s); await using var db = fixture.Create();
        db.AiCustomerMemories.Add(new() { ConversationId = s.Chat, TenantId = s.Tenant, ReviewedSummary = "Existing staff-reviewed summary",
            ReviewedById = s.Admin, Version = 5 });
        await db.SaveChangesAsync();
        var migration = new AiStructuredCustomerMemory { ActiveProvider = db.Database.ProviderName! };
        var sql = db.GetService<IMigrationsSqlGenerator>();
        foreach (var command in sql.Generate(migration.DownOperations)) await db.Database.ExecuteSqlRawAsync(command.CommandText);
        foreach (var command in sql.Generate(migration.UpOperations)) await db.Database.ExecuteSqlRawAsync(command.CommandText.Replace("{", "{{").Replace("}", "}}"));
        db.ChangeTracker.Clear();
        var memory = await db.AiCustomerMemories.SingleAsync(m => m.ConversationId == s.Chat);
        Assert.Equal("Existing staff-reviewed summary", memory.ReviewedSummary); Assert.Equal(5, memory.Version);
        Assert.Equal(s.Admin, memory.ReviewedById); Assert.False(AiCustomerMemoryDetails.Read(memory.ReviewedDetailsJson).HasContent);
    }

    [Fact]
    public async Task Structured_memory_roundtrips_without_overwriting_lead_or_booking_and_invalidates_old_draft()
    {
        var s = await SeedAsync(); var fact = await EngineSeedAsync(s);
        await using var db = fixture.Create(); var engine = Engine(db, s, GoodProvider(fact.Id).Object);
        var draft = await engine.GenerateAsync(s.Chat, s.Admin, 1, "memory-before", default);
        await using var app = await StartHttpAsync(s); using var client = app.GetTestClient(); await Authenticate(client, s.Admin, "1");
        var url = $"/api/ai-sales-engine/conversations/{s.Chat}/memory";
        var response = await client.PutAsJsonAsync(url, new { expectedVersion = 0, reviewedSummary = "Passport being renewed; customer prefers packing.",
            details = new { passportReadiness = 3, preferredWork = " Packing ", mainObjection = "Call 01234567890 or synthetic@example.test", nextStep = "Ask staff about visit", visitPreference = "Thursday afternoon; not confirmed" } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", (await client.GetAsync(url)).Headers.CacheControl!.ToString());
        db.ChangeTracker.Clear();
        var saved = await db.AiCustomerMemories.SingleAsync(m => m.ConversationId == s.Chat);
        var details = AiCustomerMemoryDetails.Read(saved.ReviewedDetailsJson);
        Assert.Equal(AiPassportReadiness.Renewing, details.PassportReadiness); Assert.Equal("Packing", details.PreferredWork);
        Assert.Equal(s.Admin, saved.ReviewedById); Assert.Equal(1, saved.Version);
        var context = await new AiSalesContextService(db).ReadAsync(s.Tenant, (await db.WhatsAppConversations.FindAsync(s.Chat))!, default);
        Assert.Equal(AiPassportReadiness.Renewing, context.Customer.ReviewedDetails!.PassportReadiness);
        Assert.DoesNotContain("01234567890", context.Customer.ReviewedDetails.MainObjection!);
        Assert.DoesNotContain("synthetic@example.test", context.Customer.ReviewedDetails.MainObjection!);
        Assert.False(await engine.IsCurrentAsync(draft, default));
        var lead = (await db.Leads.FindAsync(s.Lead))!;
        Assert.Equal((byte)48, lead.Age); Assert.Equal("Welder", lead.InterestedJobTitle); Assert.Null(lead.AppointmentDate);
        Assert.Empty(await db.AiOfficeBookings.Where(b => b.ConversationId == s.Chat).ToListAsync());
        Assert.False(await db.WhatsAppMessages.AnyAsync(m => m.ConversationId == s.Chat && m.Direction == (byte)MessageDirection.Outgoing));
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(url, new { expectedVersion = 1, reviewedSummary = "Corrected summary" })).StatusCode);
        db.ChangeTracker.Clear(); saved = await db.AiCustomerMemories.SingleAsync(m => m.ConversationId == s.Chat);
        Assert.Equal(details, AiCustomerMemoryDetails.Read(saved.ReviewedDetailsJson)); Assert.Equal(2, saved.Version);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(url, new { expectedVersion = 1, reviewedSummary = "Stale overwrite" })).StatusCode);
    }

    [Fact]
    public async Task Structured_memory_requires_reviewable_data_valid_fields_and_csrf()
    {
        var s = await SeedAsync(); await EngineSeedAsync(s);
        await using var app = await StartHttpAsync(s); using var client = app.GetTestClient(); await Authenticate(client, s.Admin, "1");
        var url = $"/api/ai-sales-engine/conversations/{s.Chat}/memory";
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PutAsJsonAsync(url, new { expectedVersion = 0, reviewedSummary = "", details = new { passportReadiness = 0 } })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PutAsJsonAsync(url, new { expectedVersion = 0, reviewedSummary = "Reviewed", details = new { passportReadiness = 99 } })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PutAsJsonAsync(url, new { expectedVersion = 0, reviewedSummary = "Reviewed", details = new { preferredWork = new string('x', 201) } })).StatusCode);
        client.DefaultRequestHeaders.Remove("RequestVerificationToken");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(url, new { expectedVersion = 0, reviewedSummary = "Unauthorized update" })).StatusCode);
        await using var db = fixture.Create(); Assert.False(await db.AiCustomerMemories.AnyAsync(m => m.ConversationId == s.Chat));
    }

    [Fact]
    public async Task Structured_memory_preserves_omitted_summary_and_can_clear_wrong_customer_details()
    {
        var s = await SeedAsync(); await EngineSeedAsync(s);
        await using var app = await StartHttpAsync(s); using var client = app.GetTestClient(); await Authenticate(client, s.Admin, "1");
        var url = $"/api/ai-sales-engine/conversations/{s.Chat}/memory";
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(url, new { expectedVersion = 0, reviewedSummary = "Reviewed summary", details = new { preferredWork = "Packing" } })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(url, new { expectedVersion = 1, details = new { passportReadiness = 1 } })).StatusCode);
        await using var db = fixture.Create();
        var memory = await db.AiCustomerMemories.SingleAsync(m => m.ConversationId == s.Chat);
        Assert.Equal("Reviewed summary", memory.ReviewedSummary);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(url, new { expectedVersion = 2, reviewedSummary = "", details = new { passportReadiness = 0 } })).StatusCode);
        db.ChangeTracker.Clear(); memory = await db.AiCustomerMemories.SingleAsync(m => m.ConversationId == s.Chat);
        Assert.Equal("", memory.ReviewedSummary); Assert.False(AiCustomerMemoryDetails.Read(memory.ReviewedDetailsJson).HasContent);
        Assert.Equal(3, memory.Version);
    }

    [Fact]
    public async Task Structured_memory_does_not_cross_tenants_or_staff_visibility()
    {
        var s = await SeedAsync(); await EngineSeedAsync(s); var other = await SeedAsync();
        await using var db = fixture.Create();
        db.AiCustomerMemories.Add(new() { ConversationId = s.Chat, TenantId = other.Tenant, ReviewedSummary = "Foreign customer context", ReviewedById = other.Admin,
            ReviewedDetailsJson = new AiCustomerMemoryDetails(PreferredWork: "Foreign preference").ToJson() });
        await db.SaveChangesAsync();
        var context = await new AiSalesContextService(db).ReadAsync(s.Tenant, (await db.WhatsAppConversations.FindAsync(s.Chat))!, default);
        Assert.Null(context.Customer.ReviewedDetails); Assert.Equal("", context.Customer.ReviewedSummary);
        await using var app = await StartHttpAsync(s); using var client = app.GetTestClient(); await Authenticate(client, s.Admin, "1");
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/api/ai-sales-engine/conversations/{s.Chat}/memory", new { expectedVersion = 1, reviewedSummary = "Foreign overwrite" })).StatusCode);
        await Authenticate(client, s.Human, "8");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/ai-sales-engine/conversations/{s.Chat}/memory")).StatusCode);
        await Authenticate(client, s.Human, "3");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/ai-sales-engine/conversations/{s.Chat}/memory")).StatusCode);
    }
}

public sealed class CustomerMemoryFormatTests
{
    [Fact]
    public void Legacy_empty_details_are_supported_but_malformed_or_unknown_values_fail_closed()
    {
        Assert.False(AiCustomerMemoryDetails.Read("{}").HasContent);
        Assert.Throws<AiSalesValidation>(() => AiCustomerMemoryDetails.Read("not json"));
        Assert.Throws<AiSalesValidation>(() => AiCustomerMemoryDetails.Read("{\"passportReadiness\":99}"));
        var details = new AiCustomerMemoryDetails(AiPassportReadiness.Applying, " Packing ", " Cost ", " Review office visit ", " Thursday ").Normalize();
        Assert.Equal(details, AiCustomerMemoryDetails.Read(details.ToJson()));
        Assert.Equal("Packing", details.PreferredWork);
    }
}
