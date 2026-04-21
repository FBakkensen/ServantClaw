using System.Text.Json;
using System.Text.Json.Serialization;

namespace ServantClaw.Codex.Transport;

internal static class CodexJsonSerialization
{
    public static readonly JsonSerializerOptions Options = BuildOptions();

    private static JsonSerializerOptions BuildOptions()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
