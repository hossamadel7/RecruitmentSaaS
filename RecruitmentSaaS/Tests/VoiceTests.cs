using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Moq;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;
using RecruitmentSaaS.Services;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace RecruitmentSaaS.Tests;

public sealed partial class FoundationTests
{
    private sealed class VoiceHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request);
    }
    private static AiVoiceOptions VoiceOptions(int quota = 100) => new() { Enabled = true, ExternalProcessingApproved = true, MaxProviderAttemptsPerDay = quota };
    private static Mock<IAiVoiceMediaDownloader> VoiceMedia()
    {
        var mock = new Mock<IAiVoiceMediaDownloader>(MockBehavior.Strict);
        mock.Setup(p => p.DownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(() =>
            new AiVoiceAudio("OggSsynthetic"u8.ToArray(), "audio/ogg", "voice.ogg", "synthetic-hash"));
        return mock;
    }
    private static Mock<IAiVoiceTranscriber> VoiceProvider(string text = "ممكن اعرف عنوان المكتب", double? probability = .95)
    {
        var mock = new Mock<IAiVoiceTranscriber>(MockBehavior.Strict);
        mock.Setup(p => p.TranscribeAsync(It.IsAny<AiVoiceAudio>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiVoiceTranscription(text, "synthetic-transcriber", probability, 10, 5));
        return mock;
    }
    private static AiVoiceService Voice(RecruitmentCrmContext db, Seed s, IAiVoiceMediaDownloader media, IAiVoiceTranscriber provider, AiVoiceOptions? options = null) =>
        new(db, Control(db, s), media, provider, Options.Create(options ?? VoiceOptions()));
    private async Task<Guid[]> AudioSeedAsync(Seed s, int count = 2, bool split = false)
    {
        await using var db = fixture.Create(); var account = (await db.WhatsAppConversations.FindAsync(s.Chat))!.WhatsAppAccountId;
        var now = DateTime.UtcNow; var ids = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            var id = Guid.NewGuid(); ids.Add(id);
            db.WhatsAppMessages.Add(new() { Id = id, ConversationId = s.Chat, WhatsAppAccountId = account, MediaId = (12345 + i).ToString(),
                Direction = (byte)MessageDirection.Incoming, MessageType = (byte)WhatsAppMessageType.Audio, Status = (byte)MessageStatus.Received,
                WhatsAppTimestamp = now.AddSeconds(-count * 3 + i * 3), CreatedAt = now.AddSeconds(i), MessageSource = (byte)MessageSource.Customer });
            if (split && i == 0) db.WhatsAppMessages.Add(new() { Id = Guid.NewGuid(), ConversationId = s.Chat, WhatsAppAccountId = account,
                Direction = (byte)MessageDirection.Outgoing, MessageType = (byte)WhatsAppMessageType.Text, Status = (byte)MessageStatus.Sent,
                TextBody = "Synthetic staff intervention", WhatsAppTimestamp = now.AddSeconds(-count * 3 + 1), CreatedAt = now.AddMilliseconds(500) });
        }
        await db.SaveChangesAsync(); return ids.ToArray();
    }
    private static async Task DueAsync(RecruitmentCrmContext db, AiVoiceTurn turn)
    { turn.AvailableAt = DateTime.UtcNow.AddSeconds(-1); await db.SaveChangesAsync(); }

    [Fact]
    public async Task Consecutive_audio_is_grouped_deduplicated_reviewed_and_used_as_one_context_entry()
    {
        var s = await SeedAsync(); var fact = await EngineSeedAsync(s); var ids = await AudioSeedAsync(s);
        await using var db = fixture.Create(); var media = VoiceMedia(); var provider = VoiceProvider(); var service = Voice(db, s, media.Object, provider.Object);
        var turn = Assert.Single(await service.QueueAsync(s.Chat, s.Admin, 1, ids, default));
        Assert.Equal(turn.Id, Assert.Single(await service.QueueAsync(s.Chat, s.Admin, 1, ids, default)).Id);
        await DueAsync(db, turn); await service.RunNextAsync(default);
        Assert.Equal(AiVoiceStatus.ReadyForReview, turn.Status); Assert.False(turn.NeedsClarification);
        var before = await new AiSalesContextService(db).ReadAsync(s.Tenant, (await db.WhatsAppConversations.FindAsync(s.Chat))!, default);
        Assert.Equal("Audio", before.RecentMessages.Last().Type);
        await service.ReviewAsync(turn.Id, s.Admin, turn.Version, "ممكن اعرف عنوان المكتب", false, default);
        var after = await new AiSalesContextService(db).ReadAsync(s.Tenant, (await db.WhatsAppConversations.FindAsync(s.Chat))!, default);
        var entry = Assert.Single(after.RecentMessages, m => m.Type == "TranscribedAudio");
        Assert.Equal(2, entry.RecordingIds!.Length); Assert.Equal(turn.Id, entry.VoiceTurnId);
        var draftProvider = GoodProvider(fact.Id);
        Assert.Equal(AiDraftStatus.ReviewRequired, (await Engine(db, s, draftProvider.Object).GenerateAsync(s.Chat, s.Admin, 1, "voice-draft", default)).Status);
        draftProvider.Verify(p => p.GenerateAsync(It.IsAny<AiSalesContext>(), It.IsAny<CancellationToken>()), Times.Once);
        provider.Verify(p => p.TranscribeAsync(It.IsAny<AiVoiceAudio>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        Assert.Equal("", (await db.WhatsAppMessages.FindAsync(ids[0]))!.TextBody ?? "");
        Assert.False(await db.WhatsAppMessages.AnyAsync(m => m.ConversationId == s.Chat && m.Direction == (byte)MessageDirection.Outgoing));
    }

    [Fact]
    public async Task Staff_intervention_splits_voice_turns_and_old_or_cross_chat_audio_is_rejected()
    {
        var s = await SeedAsync(); await EngineSeedAsync(s); var ids = await AudioSeedAsync(s, split: true);
        await using var db = fixture.Create(); var service = Voice(db, s, VoiceMedia().Object, VoiceProvider().Object);
        Assert.Equal(2, (await service.QueueAsync(s.Chat, s.Admin, 1, ids, default)).Length);
        var other = await SeedAsync(); var foreign = await AudioSeedAsync(other, 1);
        await Assert.ThrowsAsync<AiSalesValidation>(() => service.QueueAsync(s.Chat, s.Admin, 1, foreign, default));
        var old = (await db.WhatsAppMessages.FindAsync(ids[0]))!; old.WhatsAppTimestamp = DateTime.UtcNow.AddHours(-1); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<AiSalesValidation>(() => service.QueueAsync(s.Chat, s.Admin, 1, [old.Id], default));
    }

    [Fact]
    public async Task Unknown_confidence_and_critical_numbers_require_explicit_confirmation()
    {
        var s = await SeedAsync(); await EngineSeedAsync(s); var ids = await AudioSeedAsync(s, 1); await using var db = fixture.Create();
        var service = Voice(db, s, VoiceMedia().Object, VoiceProvider("موعد الساعة 3", null).Object);
        var turn = Assert.Single(await service.QueueAsync(s.Chat, s.Admin, 1, ids, default)); await DueAsync(db, turn); await service.RunNextAsync(default);
        Assert.True(turn.NeedsClarification); Assert.Null(turn.LowestTokenProbability);
        await Assert.ThrowsAsync<AiSalesValidation>(() => service.ReviewAsync(turn.Id, s.Admin, turn.Version, "موعد الساعة 3", false, default));
        await service.ReviewAsync(turn.Id, s.Admin, turn.Version, "طلب موعد الساعة 3؛ الموعد لم يتم حجزه", true, default);
        Assert.Equal(AiVoiceStatus.Reviewed, turn.Status);
    }

    [Fact]
    public async Task Human_takeover_during_provider_call_discards_voice_for_model_context()
    {
        var s = await SeedAsync(); await EngineSeedAsync(s); var ids = await AudioSeedAsync(s, 1); await using var db = fixture.Create();
        var provider = VoiceProvider();
        provider.Setup(p => p.TranscribeAsync(It.IsAny<AiVoiceAudio>(), It.IsAny<CancellationToken>())).Returns(async () =>
        {
            await using var humanDb = fixture.Create();
            await Control(humanDb, s).ControlAsync(s.Chat, s.Admin, 1, "takeover", "Voice race", "voice-takeover", default);
            return new AiVoiceTranscription("عنوان المكتب", "synthetic", .9, 10, 2);
        });
        var service = Voice(db, s, VoiceMedia().Object, provider.Object);
        var turn = Assert.Single(await service.QueueAsync(s.Chat, s.Admin, 1, ids, default)); await DueAsync(db, turn); await service.RunNextAsync(default);
        Assert.Equal(AiVoiceStatus.Stale, turn.Status); Assert.Equal(10, (await db.AiVoiceParts.FindAsync(ids[0]))!.InputTokens);
        await Assert.ThrowsAsync<AiSalesConflict>(() => service.ReviewAsync(turn.Id, s.Admin, turn.Version, "عنوان المكتب", true, default));
        Assert.DoesNotContain((await new AiSalesContextService(db).ReadAsync(s.Tenant, (await db.WhatsAppConversations.FindAsync(s.Chat))!, default)).RecentMessages, m => m.Type == "TranscribedAudio");
    }

    [Fact]
    public async Task Partial_failure_requires_manual_retry_and_does_not_repeat_completed_recordings()
    {
        var s = await SeedAsync(); await EngineSeedAsync(s); var ids = await AudioSeedAsync(s); await using var db = fixture.Create();
        var provider = VoiceProvider(); provider.SetupSequence(p => p.TranscribeAsync(It.IsAny<AiVoiceAudio>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiVoiceTranscription("عنوان", "synthetic", .9)).ThrowsAsync(new AiVoiceException("TranscriptionHttp503"))
            .ReturnsAsync(new AiVoiceTranscription("المكتب", "synthetic", .9));
        var service = Voice(db, s, VoiceMedia().Object, provider.Object);
        var turn = Assert.Single(await service.QueueAsync(s.Chat, s.Admin, 1, ids, default)); await DueAsync(db, turn); await service.RunNextAsync(default);
        Assert.Equal(AiVoiceStatus.Failed, turn.Status); await service.RunNextAsync(default);
        provider.Verify(p => p.TranscribeAsync(It.IsAny<AiVoiceAudio>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        await Assert.ThrowsAsync<AiSalesConflict>(() => service.RetryAsync(turn.Id, s.Admin, turn.Version, false, default));
        await service.RetryAsync(turn.Id, s.Admin, turn.Version, true, default); await service.RunNextAsync(default);
        Assert.Equal(AiVoiceStatus.ReadyForReview, turn.Status); Assert.Equal("عنوان\nالمكتب", turn.Transcript);
        provider.Verify(p => p.TranscribeAsync(It.IsAny<AiVoiceAudio>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task Interrupted_job_is_failed_without_automatic_paid_retry_and_quota_bounds_attempts()
    {
        var s = await SeedAsync(); await EngineSeedAsync(s); var ids = await AudioSeedAsync(s); await using var db = fixture.Create();
        var provider = VoiceProvider(); var service = Voice(db, s, VoiceMedia().Object, provider.Object, VoiceOptions(1));
        var turn = Assert.Single(await service.QueueAsync(s.Chat, s.Admin, 1, ids, default)); await DueAsync(db, turn); await service.RunNextAsync(default);
        Assert.Equal("DailyVoiceLimit", turn.ErrorCode); provider.Verify(p => p.TranscribeAsync(It.IsAny<AiVoiceAudio>(), It.IsAny<CancellationToken>()), Times.Once);
        turn.Status = AiVoiceStatus.Processing; turn.StartedAt = DateTime.UtcNow.AddHours(-1); await db.SaveChangesAsync();
        await service.RunNextAsync(default); Assert.Equal("InterruptedTranscription", turn.ErrorCode); Assert.Equal(AiVoiceStatus.Failed, turn.Status);
        provider.Verify(p => p.TranscribeAsync(It.IsAny<AiVoiceAudio>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Voice_requires_separate_approval_and_human_request_stops_automation()
    {
        var s = await SeedAsync(); await EngineSeedAsync(s); var ids = await AudioSeedAsync(s, 1); await using var db = fixture.Create();
        var media = VoiceMedia(); var provider = VoiceProvider("عايز اكلم موظف");
        await Assert.ThrowsAsync<AiSalesValidation>(() => Voice(db, s, media.Object, provider.Object, new()).QueueAsync(s.Chat, s.Admin, 1, ids, default));
        media.VerifyNoOtherCalls(); provider.VerifyNoOtherCalls();
        var service = Voice(db, s, media.Object, provider.Object); var turn = Assert.Single(await service.QueueAsync(s.Chat, s.Admin, 1, ids, default));
        await DueAsync(db, turn); await service.RunNextAsync(default);
        Assert.Equal(AiVoiceStatus.Stale, turn.Status); Assert.Equal("HumanRequested", turn.ErrorCode);
        Assert.Equal(ConversationControllerMode.Human, (await db.ConversationControls.FindAsync(s.Chat))!.Controller);
    }

    [Fact]
    public async Task Media_download_checks_host_hash_size_and_container_before_returning_audio()
    {
        var data = "OggSsynthetic"u8.ToArray(); var calls = 0;
        using var http = new HttpClient(new VoiceHandler(request =>
        {
            calls++; Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            if (request.RequestUri!.Host == "graph.facebook.com") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content = JsonContent.Create(new { id = "123", url = "https://lookaside.fbsbx.com/audio", mime_type = "audio/ogg", file_size = data.Length, sha256 = Convert.ToBase64String(SHA256.HashData(data)) }) });
            var content = new ByteArrayContent(data); content.Headers.ContentType = new("audio/ogg");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }));
        var credentials = new Mock<IMetaCredentialStore>(); credentials.Setup(c => c.GetAccessTokensAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { "synthetic-token" });
        var downloader = new MetaVoiceMediaDownloader(http, credentials.Object, new ConfigurationBuilder().Build(), Options.Create(VoiceOptions()));
        Assert.Equal(data, (await downloader.DownloadAsync("123", default)).Data); Assert.Equal(2, calls);
        Assert.False(MetaVoiceMediaDownloader.AllowedMediaUri(new("https://lookaside.fbsbx.com.attacker.test/audio")));
        Assert.False(MetaVoiceMediaDownloader.AllowedMediaUri(new("http://lookaside.fbsbx.com/audio")));
        Assert.False(MetaVoiceMediaDownloader.AllowedMediaUri(new("https://user@lookaside.fbsbx.com/audio")));
        await Assert.ThrowsAsync<AiVoiceException>(() => downloader.DownloadAsync("https://attacker.test", default));
        Assert.Throws<AiVoiceException>(() => MetaVoiceMediaDownloader.ValidateSignature("html"u8.ToArray(), "audio/ogg"));
        await Assert.ThrowsAsync<AiVoiceException>(() => MetaVoiceMediaDownloader.ReadBoundedAsync(new ByteArrayContent(data), 3, default));
    }

    [Fact]
    public async Task Transcription_contract_requests_Arabic_json_logprobs_and_preserves_unknown_confidence()
    {
        using var http = new HttpClient(new VoiceHandler(async request =>
        {
            Assert.Equal("https://api.openai.com/v1/audio/transcriptions", request.RequestUri!.ToString());
            var body = await request.Content!.ReadAsStringAsync();
            Assert.Contains("voice.ogg", body); Assert.Contains("logprobs", body); Assert.Contains("ar", body);
            Assert.DoesNotContain("synthetic-key", body);
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { text = "عنوان المكتب", usage = new { seconds = 2.5 } }) };
        }));
        var options = VoiceOptions(); options.Model = "gpt-4o-mini-transcribe"; options.ApiKey = "synthetic-key";
        var result = await new OpenAiVoiceTranscriber(http, Options.Create(options)).TranscribeAsync(new("OggSsynthetic"u8.ToArray(), "audio/ogg", "voice.ogg", ""), default);
        Assert.Null(result.LowestTokenProbability); Assert.Null(result.InputTokens); Assert.Equal(2.5, result.ReportedSeconds);
    }

    [Fact]
    public async Task Auto_queue_is_transactional_and_does_not_download_media_in_the_webhook_path()
    {
        var s = await SeedAsync(); await EngineSeedAsync(s); var ids = await AudioSeedAsync(s, 1); await using var db = fixture.Create();
        var media = VoiceMedia(); var provider = VoiceProvider(); var options = VoiceOptions(); options.AutoQueueIncoming = true;
        var service = Voice(db, s, media.Object, provider.Object, options);
        await using (var gate = await ConversationLock.AcquireAsync(db, "conversation:" + s.Chat))
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await service.QueueIncomingLockedAsync((await db.WhatsAppConversations.FindAsync(s.Chat))!, (await db.WhatsAppMessages.FindAsync(ids[0]))!, default);
            Assert.Single(await db.AiVoiceTurns.Where(t => t.ConversationId == s.Chat).ToListAsync());
            await tx.RollbackAsync();
        }
        db.ChangeTracker.Clear(); Assert.False(await db.AiVoiceTurns.AnyAsync(t => t.ConversationId == s.Chat));
        media.VerifyNoOtherCalls(); provider.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Media_hash_mime_unsafe_host_and_redirect_fail_before_transcription()
    {
        var credentials = new Mock<IMetaCredentialStore>(); credentials.Setup(c => c.GetAccessTokensAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { "synthetic-token" });
        foreach (var reason in new[] { "UnsafeMediaHost", "MediaHashMismatch", "MediaTypeMismatch", "MediaDownloadHttp302" })
        {
            var data = "OggSsynthetic"u8.ToArray(); var downloads = 0;
            using var http = new HttpClient(new VoiceHandler(request =>
            {
                if (request.RequestUri!.Host == "graph.facebook.com") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                    Content = JsonContent.Create(new { id = "123", url = reason == "UnsafeMediaHost" ? "https://attacker.test/audio" : "https://lookaside.fbsbx.com/audio",
                        mime_type = "audio/ogg", file_size = data.Length, sha256 = reason == "MediaHashMismatch" ? new string('0', 64) : Convert.ToHexString(SHA256.HashData(data)) }) });
                downloads++; var content = new ByteArrayContent(data); content.Headers.ContentType = new(reason == "MediaTypeMismatch" ? "text/html" : "audio/ogg");
                return Task.FromResult(new HttpResponseMessage(reason == "MediaDownloadHttp302" ? HttpStatusCode.Redirect : HttpStatusCode.OK) { Content = content });
            }));
            var downloader = new MetaVoiceMediaDownloader(http, credentials.Object, new ConfigurationBuilder().Build(), Options.Create(VoiceOptions()));
            Assert.Equal(reason, (await Assert.ThrowsAsync<AiVoiceException>(() => downloader.DownloadAsync("123", default))).Message);
            Assert.Equal(reason == "UnsafeMediaHost" ? 0 : 1, downloads);
        }
    }

    [Fact]
    public async Task Voice_http_routes_enforce_auth_csrf_and_scope_without_exposing_keys()
    {
        var s = await SeedAsync(); await EngineSeedAsync(s); await using var app = await StartHttpAsync(s); using var client = app.GetTestClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/ai-sales-voice/status")).StatusCode);
        await Authenticate(client, s.Human, "3"); Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/ai-sales-voice/status")).StatusCode);
        await Authenticate(client, s.Admin, "1");
        var result = await client.GetStringAsync("/api/ai-sales-voice/status"); Assert.DoesNotContain("apiKey", result);
        client.DefaultRequestHeaders.Remove("RequestVerificationToken");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/ai-sales-voice/conversations/{s.Chat}/queue", new { expectedVersion = 1, messageIds = new[] { Guid.NewGuid() } })).StatusCode);
        var other = await SeedAsync(); await Authenticate(client, other.Human, "7");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/ai-sales-voice/conversations/{s.Chat}")).StatusCode);
    }
}
