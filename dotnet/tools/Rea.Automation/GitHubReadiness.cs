using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Rea.Automation;

/// <summary>Ports the original metadata-only readiness check; never checks out PR head code.</summary>
public sealed class GitHubReadiness(HttpClient http)
{
    public async Task<ReadinessVerdict?> CheckAsync(string repository, int number, string template, string runUrl, CancellationToken cancellation = default)
    {
        if (!Regex.IsMatch(repository, @"\A[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)) || number <= 0)
            throw new ArgumentException("A repository owner/name and positive PR number are required");
        var route = $"repos/{repository}/pulls/{number}";
        var pull = PullSnapshot.Parse(await Get(route, cancellation), number);
        var baseRef = pull.BaseRef;
        if (pull.State != "open" || !(baseRef == "main" || baseRef.StartsWith("release/", StringComparison.Ordinal))) return null;
        var sha = pull.HeadSha;
        await Publish(repository, sha, new("pending", "Checking the initial author readiness declaration."), runUrl, cancellation);
        var verdict = Readiness.Evaluate(pull.Draft, pull.Body ?? "", template);
        if (verdict.State == "success")
        {
            var associated = new List<AssociatedPull>();
            for (var page = 1; ; page++)
            {
                using var response = await http.GetAsync($"repos/{repository}/commits/{sha}/pulls?per_page=100&page={page}", cancellation);
                response.EnsureSuccessStatusCode();
                var array = await response.Content.ReadFromJsonAsync<JsonArray>(cancellation) ?? throw new InvalidDataException("Missing associated PR list");
                foreach (var item in array) associated.Add(AssociatedPull.Parse(item ?? throw new InvalidDataException("Null associated PR")));
                if (array.Count < 100) break;
            }
            if (!associated.Any(item => item.Number == number && item.State == "open" && item.HeadSha == sha))
                verdict = new("pending", "GitHub has not confirmed this PR's head association; rerun PR readiness.");
            else if (associated.Any(item => item.Number != number && item.State == "open" && item.HeadSha == sha))
                verdict = new("failure", "Open PRs must have distinct head commits because GitHub shares commit statuses.");
        }
        var latest = PullSnapshot.Parse(await Get(route, cancellation), number);
        if (latest.State != "open") return null;
        if (latest != pull)
            verdict = new("pending", "PR changed during the check; wait for the latest run or rerun PR readiness.");
        await Publish(repository, latest.HeadSha, verdict, runUrl, cancellation);
        return verdict;
    }

    private async Task<JsonNode> Get(string path, CancellationToken cancellation)
    {
        using var response = await http.GetAsync(path, cancellation); response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonNode>(cancellation) ?? throw new InvalidDataException("Missing GitHub response");
    }
    private async Task Publish(string repository, string sha, ReadinessVerdict verdict, string runUrl, CancellationToken cancellation)
    {
        using var response = await http.PostAsJsonAsync($"repos/{repository}/statuses/{sha}", new
        { state = verdict.State, description = verdict.Description, context = "PR readiness", target_url = runUrl }, cancellation);
        response.EnsureSuccessStatusCode();
    }
    private static string Sha(JsonNode pull)
    {
        var sha = Text(pull, "head", "sha");
        if (!Regex.IsMatch(sha, @"\A[0-9a-f]{40}\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))) throw new InvalidDataException("Invalid PR head SHA");
        return sha;
    }
    private static string Text(JsonNode node, params string[] path)
    { JsonNode? value = node; foreach (var key in path) value = value?[key]; return value?.GetValue<string>() ?? throw new InvalidDataException("Missing PR metadata: " + string.Join('.', path)); }
    private sealed record AssociatedPull(int Number, string State, string HeadSha)
    {
        public static AssociatedPull Parse(JsonNode node)
        {
            var number = node["number"]?.GetValue<int>() ?? throw new InvalidDataException("PR number missing");
            var state = Text(node, "state");
            if (number <= 0 || state is not ("open" or "closed")) throw new InvalidDataException("Invalid PR identity/state");
            return new(number, state, Sha(node));
        }
    }
    private sealed record PullSnapshot(int Number, string State, string HeadSha, string BaseRef, bool Draft, string? Body)
    {
        public static PullSnapshot Parse(JsonNode node, int expectedNumber)
        {
            var identity = AssociatedPull.Parse(node);
            if (identity.Number != expectedNumber) throw new InvalidDataException("GitHub returned a different PR");
            if (node is not JsonObject value || !value.ContainsKey("body")) throw new InvalidDataException("PR body field missing");
            var baseRef = Text(node, "base", "ref");
            if (baseRef.Length == 0) throw new InvalidDataException("PR base ref missing");
            return new(identity.Number, identity.State, identity.HeadSha, baseRef,
                node["draft"]?.GetValue<bool>() ?? throw new InvalidDataException("PR draft state missing"), node["body"]?.GetValue<string>());
        }
    }

    public static HttpClient CreateClient(string apiUrl, string token)
    {
        var uri = new Uri(apiUrl.TrimEnd('/') + "/");
        if (uri.Scheme != "https") throw new ArgumentException("GitHub API must use HTTPS");
        var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = uri, Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("rea-dotnet-automation/0.1");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }
}
