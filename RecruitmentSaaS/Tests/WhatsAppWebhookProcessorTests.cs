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

    [Fact]
    public void Click_to_whatsapp_referral_preserves_ad_and_click_attribution()
    {
        var message = JObject.Parse("""
        {
          "type": "text",
          "text": { "body": "مهتم بالإعلان" },
          "referral": {
            "source_id": "120211234567890123",
            "source_type": "ad",
            "source_url": "https://fb.me/example",
            "headline": "فرصة عمل",
            "body": "تفاصيل الإعلان",
            "media_type": "image",
            "ctwa_clid": "ARAkExampleClickId"
          }
        }
        """);

        var referral = WhatsAppWebhookProcessor.ExtractAdReferral(message);

        Assert.NotNull(referral);
        Assert.Equal("120211234567890123", referral.SourceId);
        Assert.Equal("ARAkExampleClickId", referral.ClickId);
        Assert.Equal("ad", referral.SourceType);
        Assert.Equal("فرصة عمل", referral.Headline);
        Assert.Equal("تفاصيل الإعلان", referral.Body);
        Assert.Equal("image", referral.MediaType);
    }

    [Fact]
    public void Message_without_referral_has_no_ad_attribution()
    {
        var message = JObject.Parse("""{ "type": "text", "text": { "body": "hello" } }""");

        Assert.Null(WhatsAppWebhookProcessor.ExtractAdReferral(message));
    }
}
