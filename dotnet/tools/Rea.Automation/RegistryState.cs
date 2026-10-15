using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Rea.Automation;

public static class RegistryState
{
    public static async Task<string> LookupAsync(HttpClient http, JsonObject server, CancellationToken cancellation = default)
    {
        var name = server["name"]?.GetValue<string>() ?? throw new InvalidDataException("Server name required");
        var version = server["version"]?.GetValue<string>() ?? throw new InvalidDataException("Server version required");
        var seen = new HashSet<string>(StringComparer.Ordinal); string? cursor = null; JsonNode? match = null;
        do
        {
            var path = "v0.1/servers?search=" + Uri.EscapeDataString(name) + "&version=" + Uri.EscapeDataString(version) + "&include_deleted=true&limit=100";
            if (cursor is not null) path += "&cursor=" + Uri.EscapeDataString(cursor);
            using var response = await http.GetAsync(path, cancellation); response.EnsureSuccessStatusCode();
            var page = await response.Content.ReadFromJsonAsync<JsonObject>(cancellation) ?? throw new InvalidDataException("Missing registry response");
            var entries = page["servers"] as JsonArray ?? throw new InvalidDataException("Registry response omitted servers");
            var metadata = page["metadata"] as JsonObject ?? throw new InvalidDataException("Registry response omitted metadata");
            foreach (var entry in entries)
            {
                var candidate = entry?["server"] as JsonObject ?? throw new InvalidDataException("Malformed registry record");
                var candidateName = candidate["name"]?.GetValue<string>() ?? throw new InvalidDataException("Registry name missing");
                var candidateVersion = candidate["version"]?.GetValue<string>() ?? throw new InvalidDataException("Registry version missing");
                if (candidateName == name && candidateVersion == version)
                { if (match is not null) throw new InvalidDataException("Ambiguous exact registry version"); match = entry; }
            }
            if (metadata.TryGetPropertyValue("nextCursor", out var next))
                cursor = next?.GetValue<string>() ?? throw new InvalidDataException("Registry cursor must be a nonempty string when present");
            else cursor = null;
            if (cursor is not null && (cursor.Length == 0 || !seen.Add(cursor))) throw new InvalidDataException("Registry cursor did not advance");
        } while (cursor is not null);
        if (match is null) return "absent";
        if (match["_meta"]?["io.modelcontextprotocol.registry/official"]?["status"]?.GetValue<string>() != "active")
            throw new InvalidDataException("Existing registry version is not active");
        if (!JsonNode.DeepEquals(match["server"], server)) throw new InvalidDataException("Existing registry metadata differs");
        return "present";
    }
}
