using Npgsql;

namespace RecruitmentSaaS.Services;

/// <summary>Opt-in smoke-test mode. Production defaults are unchanged.</summary>
public static class StagingValidationIsolation
{
    public static bool Validate(IConfiguration configuration, string environment)
    {
        if (!configuration.GetValue<bool>("StagingValidation:Enabled")) return false;
        if (environment != "Staging") throw new InvalidOperationException("Validation isolation requires the Staging environment.");
        var connection = new NpgsqlConnectionStringBuilder(configuration.GetConnectionString("DefaultConnection"));
        if (connection.Host != "127.0.0.1" || connection.Port != 55441 ||
            connection.Database is not { } name || !name.StartsWith("validation_", StringComparison.Ordinal))
            throw new InvalidOperationException("Validation isolation requires the dedicated loopback database on port 55441.");
        if (new[] { "AiSales:Enabled", "AiSalesEngine:DraftsEnabled", "AiSalesEngine:ExternalProcessingApproved",
            "AiSalesDispatch:Enabled", "AiSalesDispatch:ReleaseApproved", "AiVoice:Enabled",
            "AiVoice:ExternalProcessingApproved", "AiChatLearning:Enabled", "AiOfficeBooking:Enabled" }
            .Any(configuration.GetValue<bool>))
            throw new InvalidOperationException("Validation isolation requires AI and external-processing gates disabled.");
        return true;
    }

    public sealed class BlockOutboundHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Outbound HTTP is disabled in staging validation.");
    }
}
