using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DataCollection.Infrastructure.Json;

/// <summary>
/// Custom JSON converter for Jira datetime format: "2024-08-28T23:15:59.000+0000"
/// Handles the specific timezone format used by Jira REST API
/// </summary>
public class JiraDateTimeConverter : JsonConverter<DateTime>
{
    private static readonly string[] DateFormats =
    [
        "yyyy-MM-ddTHH:mm:ss.fffzzz", // 2024-08-28T23:15:59.000+00:00
        "yyyy-MM-ddTHH:mm:ss.fff+0000", // 2024-08-28T23:15:59.000+0000
        "yyyy-MM-ddTHH:mm:ss.fff-0000", // 2024-08-28T23:15:59.000-0000
        "yyyy-MM-ddTHH:mm:sszzz", // 2024-08-28T23:15:59+00:00
        "yyyy-MM-ddTHH:mm:ss+0000", // 2024-08-28T23:15:59+0000
        "yyyy-MM-ddTHH:mm:ss-0000", // 2024-08-28T23:15:59-0000
        "yyyy-MM-ddTHH:mm:ss.fffZ", // 2024-08-28T23:15:59.000Z
        "yyyy-MM-ddTHH:mm:ssZ", // 2024-08-28T23:15:59Z
    ];

    public override DateTime Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        var dateString = reader.GetString();
        if (string.IsNullOrEmpty(dateString))
            return DateTime.MinValue;

        // Try each format until one works
        foreach (var format in DateFormats)
        {
            if (
                DateTime.TryParseExact(
                    dateString,
                    format,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal,
                    out var result
                )
            )
            {
                return result;
            }
        }

        // Fallback: try standard ISO 8601 parsing
        if (
            DateTime.TryParse(
                dateString,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal,
                out var fallbackResult
            )
        )
        {
            return fallbackResult;
        }

        throw new JsonException($"Unable to parse Jira datetime: {dateString}");
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        // Use standard ISO format for writing
        writer.WriteStringValue(
            value.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture)
        );
    }
}

/// <summary>
/// Nullable version of JiraDateTimeConverter
/// </summary>
public class NullableJiraDateTimeConverter : JsonConverter<DateTime?>
{
    private readonly JiraDateTimeConverter _innerConverter = new();

    public override DateTime? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        return _innerConverter.Read(ref reader, typeof(DateTime), options);
    }

    public override void Write(
        Utf8JsonWriter writer,
        DateTime? value,
        JsonSerializerOptions options
    )
    {
        if (value.HasValue)
            _innerConverter.Write(writer, value.Value, options);
        else
            writer.WriteNullValue();
    }
}
