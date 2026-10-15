namespace RecruitmentSaaS.Services;

public sealed class AiVoiceOptions
{
    public bool Enabled { get; set; }
    public bool ExternalProcessingApproved { get; set; }
    public bool AutoQueueIncoming { get; set; }
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "";
    public int MaxBytes { get; set; } = 10 * 1024 * 1024;
    public int MaxPartsPerTurn { get; set; } = 4;
    public int MaxProviderAttemptsPerDay { get; set; } = 100;
    public int GroupGapSeconds { get; set; } = 30;
    public int QuietSeconds { get; set; } = 10;
    public int MaxMessageAgeMinutes { get; set; } = 15;
    public double MinimumTokenProbability { get; set; } = 0.7;
}

public sealed class AiVoiceException(string code) : Exception(code);
public sealed record AiVoiceAudio(byte[] Data, string ContentType, string FileName, string Sha256);
public sealed record AiVoiceTranscription(string Text, string Model, double? LowestTokenProbability,
    int? InputTokens = null, int? OutputTokens = null, double? ReportedSeconds = null);
public interface IAiVoiceMediaDownloader
{
    Task<AiVoiceAudio> DownloadAsync(string mediaId, CancellationToken ct);
}
public interface IAiVoiceTranscriber
{
    Task<AiVoiceTranscription> TranscribeAsync(AiVoiceAudio audio, CancellationToken ct);
}
