using Newtonsoft.Json.Linq;
using RecruitmentSaaS.Models.Entities;
using RecruitmentSaaS.Services;
using Xunit;

namespace RecruitmentSaaS.Tests;

public sealed class WhatsAppWebhookProcessorTests
{
    [Fact]
    public void Unsupported_message_never_runs_customer_automation()
    {
        Assert.False(WhatsAppWebhookProcessor.ShouldRunCustomerAutomation(WhatsAppMessageType.Unsupported));
        Assert.True(WhatsAppWebhookProcessor.ShouldRunCustomerAutomation(WhatsAppMessageType.Text));
        Assert.True(WhatsAppWebhookProcessor.ShouldRunCustomerAutomation(WhatsAppMessageType.Audio));
    }

    [Fact]
    public void Unsupported_diagnostics_preserve_Meta_code_title_details_and_type()
    {
        var message = JObject.Parse("""
        {
          "type": "unsupported",
          "unsupported": { "type": "poll" },
          "errors": [{
            "code": 131051,
            "title": "Unsupported message type",
            "details": "Message type is not currently supported"
          }]
        }
        """);

        var (code, details) = WhatsAppWebhookProcessor.ExtractUnsupportedDiagnostics(message);

        Assert.Equal("131051", code);
        Assert.Equal("Unsupported message type | Message type is not currently supported | poll", details);
        Assert.Contains("استطلاع رأي", WhatsAppWebhookProcessor.UnsupportedText(message));
    }
}
