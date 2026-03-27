using System.Text.Json;
using System.Text.Json.Serialization;

namespace AppMigrator.UI.Helpers;

public static class JsonHelper
{
    public static readonly JsonSerializerOptions DefaultOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
