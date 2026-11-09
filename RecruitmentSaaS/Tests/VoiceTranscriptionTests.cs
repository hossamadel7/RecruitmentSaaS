using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;
using RecruitmentSaaS.Services;
using Xunit;

namespace RecruitmentSaaS.Tests;

public sealed partial class FoundationTests
{
    private static AiVoiceOptions AutoVoiceOptions() => new() { Enabled = true, ExternalProcessingApproved = true,
        TranscribeAllIncoming = true, MaxProviderAttemptsPerDay = 1000 };

    private static VoiceTranscriptionService Transcription(RecruitmentCrmContext db, Seed s, IAiVoiceTranscriber provider, Mock<IInboxRealtimeNotifier>? notifier = null) =>
        new(db, Control(db, s), VoiceMedia().Object, provider, (notifier ?? new Mock<IInboxRealtimeNotifier>()).Object,
            Options.Create(AutoVoiceOptions()), NullLogger<VoiceTranscriptionService>.Instance);

    // The test database is shared, so the worker also handles voice notes left by earlier tests: run it until idle.
    private static async Task<int> TranscribeAllAsync(VoiceTranscriptionService service)
    {
        int total = 0, handled;
        while ((handled = await service.RunAsync(default)) > 0) total += handled;
        return total;
    }

    private async Task<Guid> VoiceNoteAsync(Seed s, int secondsAgo = 10)
    {
        await using var db = fixture.Create();
        var chat = (await db.WhatsAppConversations.FindAsync(s.Chat))!;
        var at = DateTime.UtcNow.AddSeconds(-secondsAgo);
        var id = Guid.NewGuid();
        db.WhatsAppMessages.Add(new() { Id = id, ConversationId = s.Chat, WhatsAppAccountId = chat.WhatsAppAccountId, MediaId = "98765",
            Direction = (byte)MessageDirection.Incoming, MessageType = (byte)WhatsAppMessageType.Audio, Status = (byte)MessageStatus.Received,
            MessageSource = (byte)MessageSource.Customer, WhatsAppTimestamp = at, CreatedAt = at });
        await db.SaveChangesAsync();
        return id;
    }

    [Fact]
    public async Task Voice_note_is_transcribed_and_the_AI_waits_for_the_text_then_replies_to_it()
    {
        var s = await SeedAsync(); var fact = await EngineSeedAsync(s);
        await using var db = fixture.Create();
        var typed = await db.WhatsAppMessages.SingleAsync(m => m.ConversationId == s.Chat);
        typed.CreatedAt = typed.WhatsAppTimestamp = DateTime.UtcNow.AddMinutes(-1); await db.SaveChangesAsync();
        var voice = await VoiceNoteAsync(s);

        var settings = new AiSalesEngineOptions { DraftsEnabled = true, ExternalProcessingApproved = true, AutoReplyEnabled = true,
            AutoReplySinceUtc = DateTime.UtcNow.AddMinutes(-5), AutoReplyAgentIds = [s.Agent], ApprovedConversationIds = [s.Chat] };
        var drafts = new AiSalesDraftService(db, Control(db, s), new(db), GoodProvider(fact.Id).Object, Options.Create(settings));
        var release = new AiSalesDispatchOptions { Enabled = true, ReleaseApproved = true, AutomaticEnabled = true,
            ApprovedConversationIds = [s.Chat], ApprovedAccountIds = [typed.WhatsAppAccountId] };
        var transport = AcceptedTransport();
        var dispatch = new AiSalesDispatchService(db, Control(db, s), drafts, transport.Object, Mock.Of<IInboxRealtimeNotifier>(), Options.Create(release));
        var automatic = new AiAutomaticReplyService(db, Control(db, s), drafts, dispatch, Options.Create(settings), Options.Create(release),
            voiceOptions: Options.Create(AutoVoiceOptions()));

        // While the voice note has no text yet, the AI does not answer with "please write".
        await automatic.RunConversationAsync(s.Chat, default);
        Assert.False(await db.AiSalesDrafts.AnyAsync(d => d.ConversationId == s.Chat));

        var notifier = new Mock<IInboxRealtimeNotifier>();
        Assert.True(await TranscribeAllAsync(Transcription(db, s, VoiceProvider("ممكن اعرف عنوان المكتب").Object, notifier)) >= 1);
        db.ChangeTracker.Clear();
        Assert.Equal("ممكن اعرف عنوان المكتب", (await db.WhatsAppMessages.FindAsync(voice))!.TextBody);
        notifier.Verify(n => n.MessageTranscribedAsync(voice, s.Chat, "ممكن اعرف عنوان المكتب", It.IsAny<Guid?>()), Times.Once);

        var context = await new AiSalesContextService(db).ReadAsync(s.Tenant, (await db.WhatsAppConversations.FindAsync(s.Chat))!, default);
        Assert.Equal(("TranscribedAudio", "ممكن اعرف عنوان المكتب"), (context.RecentMessages.Last().Type, context.RecentMessages.Last().Text));

        await automatic.RunConversationAsync(s.Chat, default);
        Assert.Single(await db.AiSalesDrafts.Where(d => d.ConversationId == s.Chat).ToListAsync());
        transport.Verify(t => t.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);

        // Already transcribed: nothing more to do.
        Assert.Equal(0, await Transcription(db, s, new Mock<IAiVoiceTranscriber>(MockBehavior.Strict).Object).RunAsync(default));
    }

    [Fact]
    public async Task Failed_voice_note_falls_back_and_a_spoken_request_for_a_person_stops_the_AI()
    {
        var s = await SeedAsync(collecting: true); await EngineSeedAsync(s);
        await using var db = fixture.Create();
        var typed = await db.WhatsAppMessages.SingleAsync(m => m.ConversationId == s.Chat);
        typed.CreatedAt = typed.WhatsAppTimestamp = DateTime.UtcNow.AddMinutes(-1); await db.SaveChangesAsync();
        var failed = await VoiceNoteAsync(s, 20);
        var broken = new Mock<IAiVoiceTranscriber>(MockBehavior.Strict);
        broken.Setup(p => p.TranscribeAsync(It.IsAny<AiVoiceAudio>(), It.IsAny<CancellationToken>())).ThrowsAsync(new AiVoiceException("TranscriptionHttp500"));
        await TranscribeAllAsync(Transcription(db, s, broken.Object));
        db.ChangeTracker.Clear();
        Assert.Equal("", (await db.WhatsAppMessages.FindAsync(failed))!.TextBody);
        Assert.False(await VoiceTranscriptionService.AnyPendingAsync(db, AutoVoiceOptions(), s.Chat, default));
        var context = await new AiSalesContextService(db).ReadAsync(s.Tenant, (await db.WhatsAppConversations.FindAsync(s.Chat))!, default);
        Assert.Equal("Audio", context.RecentMessages.Last().Type); // The AI asks the customer to write instead.

        var spoken = await VoiceNoteAsync(s, 5);
        Assert.True(await VoiceTranscriptionService.AnyPendingAsync(db, AutoVoiceOptions(), s.Chat, default));
        await TranscribeAllAsync(Transcription(db, s, VoiceProvider("لو سمحت عايز اكلم حد من الشركة").Object));
        db.ChangeTracker.Clear();
        Assert.Equal(ConversationControllerMode.Human, (await db.ConversationControls.FindAsync(s.Chat))!.Controller);
        // The intake assistant that waited for the text gets its turn again.
        Assert.True(await db.DurableWorkItems.AnyAsync(w => w.DeduplicationKey == "intake-voice:" + failed));
        Assert.False(await db.DurableWorkItems.AnyAsync(w => w.DeduplicationKey == "intake-voice:" + spoken)); // takeover stopped intake
    }

    [Fact]
    public void Voice_notes_only_wait_for_text_when_automatic_transcription_is_on_and_recent()
    {
        var now = DateTime.UtcNow;
        var note = new WhatsAppMessage { Direction = (byte)MessageDirection.Incoming, MessageType = (byte)WhatsAppMessageType.Audio, MediaId = "1", CreatedAt = now.AddSeconds(-5) };
        Assert.True(VoiceTranscriptionService.IsPending(AutoVoiceOptions(), note, now));
        Assert.False(VoiceTranscriptionService.IsPending(null, note, now));
        Assert.False(VoiceTranscriptionService.IsPending(new AiVoiceOptions { Enabled = true, ExternalProcessingApproved = true }, note, now));
        Assert.False(VoiceTranscriptionService.IsPending(AutoVoiceOptions(), new WhatsAppMessage { Direction = 1, MessageType = 4, MediaId = "1", CreatedAt = now.AddMinutes(-3) }, now));
        Assert.False(VoiceTranscriptionService.IsPending(AutoVoiceOptions(), new WhatsAppMessage { Direction = 1, MessageType = 4, MediaId = "1", TextBody = "", CreatedAt = now }, now));
    }
}
