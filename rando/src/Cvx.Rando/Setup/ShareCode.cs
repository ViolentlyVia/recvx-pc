using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Cvx.Rando.Setup;

/// <summary>
/// A seed and its settings as one string to share: "CVX1-" and the settings that differ from
/// the defaults as JSON, deflated and base64url-encoded. Settings left out (defaults, or ones an
/// older version did not have) get their defaults.
/// </summary>
public static class ShareCode
{
    public const string Prefix = "CVX1-";

    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Encode(RandoSettings settings)
    {
        // only what differs from the defaults (missing settings decode to them)
        var all = JsonSerializer.SerializeToNode(settings, Options)!.AsObject();
        var defaults = JsonSerializer.SerializeToNode(new RandoSettings(), Options)!.AsObject();
        foreach (var (name, value) in defaults.ToList())
        {
            if (name != nameof(RandoSettings.Seed) && JsonNode.DeepEquals(all[name], value))
                all.Remove(name);
        }
        var json = Encoding.UTF8.GetBytes(all.ToJsonString());
        using var ms = new MemoryStream();
        using (var deflate = new DeflateStream(ms, CompressionLevel.SmallestSize, leaveOpen: true))
            deflate.Write(json);
        return Prefix + Convert.ToBase64String(ms.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static bool TryDecode(string text, out RandoSettings? settings, out string? error)
    {
        settings = null;
        error = null;
        var code = text.Trim();
        if (!code.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            error = $"A share code starts with {Prefix}";
            return false;
        }
        try
        {
            var b64 = code[Prefix.Length..].Replace('-', '+').Replace('_', '/');
            b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
            using var input = new DeflateStream(new MemoryStream(Convert.FromBase64String(b64)), CompressionMode.Decompress);
            using var output = new MemoryStream();
            input.CopyTo(output);
            settings = JsonSerializer.Deserialize<RandoSettings>(Encoding.UTF8.GetString(output.ToArray()), Options);
            if (settings == null)
                error = "The share code is empty.";
            return settings != null;
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException or JsonException)
        {
            error = "That share code is not valid.";
            return false;
        }
    }
}
