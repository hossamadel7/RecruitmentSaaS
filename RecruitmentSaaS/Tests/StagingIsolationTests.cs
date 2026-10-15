using Microsoft.Extensions.Configuration;
using RecruitmentSaaS.Services;
using Xunit;

namespace RecruitmentSaaS.Tests;

public sealed class StagingIsolationTests
{
    private static IConfiguration Configuration(string connection, bool enabled = true, string? activeGate = null)
    {
        var values = new Dictionary<string, string?> { ["StagingValidation:Enabled"] = enabled.ToString(), ["ConnectionStrings:DefaultConnection"] = connection };
        if (activeGate != null) values[activeGate] = "true";
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public void Normal_production_configuration_does_not_enable_validation_mode()
        => Assert.False(StagingValidationIsolation.Validate(Configuration("Host=production;Database=RecruitmentCRM", false), "Production"));

    [Theory]
    [InlineData("Production", "Host=127.0.0.1;Port=55441;Database=validation_smoke")]
    [InlineData("Staging", "Host=127.0.0.1;Port=5432;Database=RecruitmentCRM")]
    [InlineData("Staging", "Host=127.0.0.1;Port=55441;Database=RecruitmentCRM")]
    [InlineData("Staging", "Host=remote;Port=55441;Database=validation_smoke")]
    public void Validation_mode_rejects_wrong_environment_or_database(string environment, string connection)
        => Assert.Throws<InvalidOperationException>(() => StagingValidationIsolation.Validate(Configuration(connection), environment));

    [Theory]
    [InlineData("AiSales:Enabled")]
    [InlineData("AiSalesEngine:ExternalProcessingApproved")]
    [InlineData("AiSalesDispatch:ReleaseApproved")]
    [InlineData("AiVoice:Enabled")]
    [InlineData("AiChatLearning:Enabled")]
    [InlineData("AiOfficeBooking:Enabled")]
    public void Validation_mode_requires_disabled_integration_gates(string gate)
        => Assert.Throws<InvalidOperationException>(() => StagingValidationIsolation.Validate(Configuration("Host=127.0.0.1;Port=55441;Database=validation_smoke", activeGate: gate), "Staging"));

    [Fact]
    public async Task Dedicated_validation_mode_accepts_isolated_target_and_blocks_HTTP()
    {
        Assert.True(StagingValidationIsolation.Validate(Configuration("Host=127.0.0.1;Port=55441;Database=validation_smoke"), "Staging"));
        using var client = new HttpClient(new StagingValidationIsolation.BlockOutboundHandler());
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetAsync("https://example.invalid"));
    }
}
