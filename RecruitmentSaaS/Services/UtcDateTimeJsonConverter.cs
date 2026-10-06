using System.Text.Json;
using System.Text.Json.Serialization;

namespace RecruitmentSaaS.Services
{
    /// <summary>
    /// The app stores every DateTime as UTC, but Npgsql's legacy timestamp mode reads them back as Kind=Unspecified,
    /// so they were sent to the browser without a "Z" and shown as if they were local time (3 hours early in Egypt).
    /// This writes them as UTC ("…Z") so the browser converts them to the viewer's local time.
    /// </summary>
    public sealed class UtcDateTimeJsonConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = reader.GetDateTime();
            return value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value;
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            var utc = value.Kind switch
            {
                DateTimeKind.Local => value.ToUniversalTime(),
                DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
                _ => value
            };
            writer.WriteStringValue(utc);
        }
    }
}
