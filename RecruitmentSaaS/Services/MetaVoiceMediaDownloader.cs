using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RecruitmentSaaS.Services;

/// <summary>Only server-resolved Meta media. No redirects, user URLs, disk files or browser credentials.</summary>
public sealed class MetaVoiceMediaDownloader(HttpClient http, IMetaCredentialStore credentials,
    IConfiguration config, IOptions<AiVoiceOptions> options) : IAiVoiceMediaDownloader
{
    public static bool AllowedMediaUri(Uri uri) => uri.Scheme == "https" && uri.Port == 443 && string.IsNullOrEmpty(uri.UserInfo)
        && (uri.Host.Equals("lookaside.fbsbx.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".fbcdn.net", StringComparison.OrdinalIgnoreCase));

    public async Task<AiVoiceAudio> DownloadAsync(string mediaId, CancellationToken ct)
    {
        if (!options.Value.Enabled || !options.Value.ExternalProcessingApproved) throw new AiVoiceException("ExternalVoiceProcessingDisabled");
        if (!Regex.IsMatch(mediaId, @"^\d{1,100}$", RegexOptions.CultureInvariant)) throw new AiVoiceException("InvalidMediaId");
        var version = config["WhatsApp:ApiVersion"] ?? "v21.0";
        if (!Regex.IsMatch(version, @"^v\d+\.\d+$", RegexOptions.CultureInvariant)) throw new AiVoiceException("InvalidGraphVersion");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        foreach (var token in await credentials.GetAccessTokensAsync(timeout.Token))
        {
            using var metadataRequest = new HttpRequestMessage(HttpMethod.Get, $"https://graph.facebook.com/{version}/{mediaId}");
            metadataRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var metadataResponse = await http.SendAsync(metadataRequest, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if ((int)metadataResponse.StatusCode is 401 or 403 or 404) continue;
            if (!metadataResponse.IsSuccessStatusCode) throw new AiVoiceException("MediaMetadataHttp" + (int)metadataResponse.StatusCode);
            using var metadata = JsonDocument.Parse(await ReadBoundedAsync(metadataResponse.Content, 16384, timeout.Token));
            var m = metadata.RootElement;
            if (m.GetProperty("id").GetString() != mediaId) throw new AiVoiceException("MediaIdMismatch");
            if (!Uri.TryCreate(m.GetProperty("url").GetString(), UriKind.Absolute, out var uri) || !AllowedMediaUri(uri))
                throw new AiVoiceException("UnsafeMediaHost");
            var mime = MediaTypeHeaderValue.Parse(m.GetProperty("mime_type").GetString()!).MediaType!;
            var extension = Extension(mime);
            if (m.GetProperty("file_size").GetInt64() is var size && (size <= 0 || size > options.Value.MaxBytes)) throw new AiVoiceException("MediaTooLarge");
            var expected = DecodeHash(m.GetProperty("sha256").GetString()!);
            using var audioRequest = new HttpRequestMessage(HttpMethod.Get, uri);
            audioRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await http.SendAsync(audioRequest, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode) throw new AiVoiceException("MediaDownloadHttp" + (int)response.StatusCode);
            if (response.Content.Headers.ContentType?.MediaType != mime) throw new AiVoiceException("MediaTypeMismatch");
            var data = await ReadBoundedAsync(response.Content, options.Value.MaxBytes, timeout.Token);
            if (data.Length != size) throw new AiVoiceException("MediaSizeMismatch");
            if (!CryptographicOperations.FixedTimeEquals(expected, SHA256.HashData(data))) throw new AiVoiceException("MediaHashMismatch");
            ValidateSignature(data, mime);
            return new(data, mime, "voice" + extension, Convert.ToHexString(expected));
        }
        throw new AiVoiceException("MediaUnavailable");
    }

    public static string Extension(string mime) => mime switch
    {
        "audio/ogg" => ".ogg", "audio/mpeg" => ".mp3", "audio/wav" or "audio/x-wav" => ".wav",
        "audio/mp4" or "audio/m4a" or "audio/x-m4a" => ".m4a", "audio/webm" => ".webm", "audio/flac" => ".flac",
        _ => throw new AiVoiceException("UnsupportedAudioType")
    };
    public static void ValidateSignature(byte[] data, string mime)
    {
        var valid = mime switch
        {
            "audio/ogg" => data.AsSpan().StartsWith("OggS"u8),
            "audio/wav" or "audio/x-wav" => data.Length >= 12 && data.AsSpan(0, 4).SequenceEqual("RIFF"u8) && data.AsSpan(8, 4).SequenceEqual("WAVE"u8),
            "audio/mpeg" => data.AsSpan().StartsWith("ID3"u8) || (data.Length > 1 && data[0] == 0xff && (data[1] & 0xe0) == 0xe0),
            "audio/webm" => data.AsSpan().StartsWith(new byte[] { 0x1a, 0x45, 0xdf, 0xa3 }),
            "audio/mp4" or "audio/m4a" or "audio/x-m4a" => data.Length >= 12 && data.AsSpan(4, 4).SequenceEqual("ftyp"u8),
            "audio/flac" => data.AsSpan().StartsWith("fLaC"u8),
            _ => false
        };
        if (!valid) throw new AiVoiceException("InvalidAudioContainer");
    }
    private static byte[] DecodeHash(string value)
    {
        try
        {
            var hash = value.Length == 64 ? Convert.FromHexString(value) : Convert.FromBase64String(value);
            if (hash.Length != 32) throw new AiVoiceException("InvalidMediaHash");
            return hash;
        }
        catch (FormatException) { throw new AiVoiceException("InvalidMediaHash"); }
    }
    public static async Task<byte[]> ReadBoundedAsync(HttpContent content, int max, CancellationToken ct)
    {
        if (content.Headers.ContentLength > max) throw new AiVoiceException("MediaTooLarge");
        await using var stream = await content.ReadAsStreamAsync(ct); using var buffer = new MemoryStream();
        var chunk = new byte[8192]; int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > max) throw new AiVoiceException("MediaTooLarge");
            await buffer.WriteAsync(chunk.AsMemory(0, read), ct);
        }
        return buffer.ToArray();
    }
}
