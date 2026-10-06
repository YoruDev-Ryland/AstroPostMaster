using System.Text.Json;
using System.Text.Json.Serialization;

namespace AstroPostMaster.Core.Storage;

public static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };
}
