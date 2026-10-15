using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text.Json;

namespace RecruitmentSaaS.Services;

public sealed class OpenAiVoiceTranscriber(HttpClient http, IOptions<AiVoiceOptions> options) : IAiVoiceTranscriber
{
    public async Task<AiVoiceTranscription> TranscribeAsync(AiVoiceAudio audio, CancellationToken ct)
    {
        var o = options.Value;
        if (!o.Enabled || !o.ExternalProcessingApproved || string.IsNullOrWhiteSpace(o.ApiKey) || !SupportedModel(o.Model))
            throw new AiVoiceException("TranscriberNotConfigured");
        if (audio.Data.Length == 0 || audio.Data.Length > o.MaxBytes) throw new AiVoiceException("MediaTooLarge");
        MetaVoiceMediaDownloader.ValidateSignature(audio.Data, audio.ContentType);
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(audio.Data); file.Headers.ContentType = new MediaTypeHeaderValue(audio.ContentType);
        form.Add(file, "file", "voice" + MetaVoiceMediaDownloader.Extension(audio.ContentType));
        form.Add(new StringContent(o.Model), "model"); form.Add(new StringContent("ar"), "language");
        form.Add(new StringContent("json"), "response_format"); form.Add(new StringContent("logprobs"), "include[]");
        form.Add(new StringContent("الكلام باللهجة المصرية. انقل الكلام كما سمعته، بدون استكمال كلمات أو أرقام غير واضحة وبدون إضافة معلومات."), "prompt");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/audio/transcriptions") { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", o.ApiKey);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(60));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (!response.IsSuccessStatusCode) throw new AiVoiceException("TranscriptionHttp" + (int)response.StatusCode);
        using var document = JsonDocument.Parse(await MetaVoiceMediaDownloader.ReadBoundedAsync(response.Content, 131072, timeout.Token));
        var root = document.RootElement; var text = root.GetProperty("text").GetString() ?? "";
        if (string.IsNullOrWhiteSpace(text) || text.Length > 2000) throw new AiVoiceException("EmptyOrLongTranscript");
        double? lowest = null;
        if (root.TryGetProperty("logprobs", out var scores))
        {
            var values = scores.EnumerateArray().Select(x => x.TryGetProperty("logprob", out var score) && score.TryGetDouble(out var value) ? value : double.NaN).ToArray();
            if (values.Length > 0 && values.All(x => double.IsFinite(x) && x <= 0)) lowest = Math.Exp(values.Min());
        }
        int? input = null, output = null; double? seconds = null;
        if (root.TryGetProperty("usage", out var usage))
        {
            if (usage.TryGetProperty("input_tokens", out var i) && i.TryGetInt32(out var iv) && iv >= 0) input = iv;
            if (usage.TryGetProperty("output_tokens", out var t) && t.TryGetInt32(out var tv) && tv >= 0) output = tv;
            if (usage.TryGetProperty("seconds", out var s) && s.TryGetDouble(out var sv) && double.IsFinite(sv) && sv >= 0) seconds = sv;
        }
        return new(text, o.Model, lowest, input, output, seconds);
    }
    public static bool SupportedModel(string model) => model is "gpt-4o-transcribe" or "gpt-4o-mini-transcribe" or "gpt-4o-mini-transcribe-2025-12-15";
}
