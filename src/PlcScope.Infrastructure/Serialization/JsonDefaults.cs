namespace PlcScope.Infrastructure.Serialization;

using System.Text.Json;
using System.Text.Json.Serialization;

internal static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    static JsonDefaults()
    {
        Options.Converters.Add(new JsonStringEnumConverter());
    }
}
