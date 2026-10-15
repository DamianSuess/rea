using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Rea.Automation;

namespace Rea.Tests;

internal static class AutomationTests
{
    private const string Template = "## Author readiness\n\nHuman owner:\n\n- [ ] I reviewed the final diff.\n- [ ] I checked the evidence.\n- [ ] This PR is ready.\n";
    private static string Body => Template.Replace("Human owner:", "Human owner: @human-owner", StringComparison.Ordinal).Replace("[ ]", "[x]", StringComparison.Ordinal);
    public static IEnumerable<Case> Cases()
    {
        yield return Case.Sync("C# readiness preserves three exact declarations and a named human owner", () =>
        {
            Check.Equal("success", Readiness.Evaluate(false, Body, Template).State);
            Check.Equal("pending", Readiness.Evaluate(true, "", Template).State);
            Check.Equal("failure", Readiness.Evaluate(false, Template, Template).State);
            Check.Equal("failure", Readiness.Evaluate(false, Body.Replace("[x] I checked", "[ ] I checked", StringComparison.Ordinal), Template).State);
            Check.Equal("failure", Readiness.Evaluate(false, Body + "- [x] I reviewed the final diff.\n", Template).State);
        });
        yield return Case.Sync("Readiness declarations in comments or code blocks cannot attest review", () =>
        {
            foreach (var hidden in new[] { "<!--\n" + Body + "\n-->", "```markdown\n" + Body + "\n```", "~~~~\n" + Body + "\n~~~~" })
                Check.Equal("failure", Readiness.Evaluate(false, hidden, Template).State);
            Check.Equal("failure", Readiness.Evaluate(false, Body + "\n" + Body, Template).State);
            Check.Equal("failure", Readiness.Evaluate(false, Body.Replace("@human-owner", "@one @two", StringComparison.Ordinal), Template).State);
        });
        yield return Case.Sync("Conventional PR titles preserve breaking change markers and reject multiline titles", () =>
        {
            foreach (var title in new[] { "feat: add static metadata", "fix(managed)!: reject invalid streams", "ci: use .NET 10" }) Check.True(Readiness.ConventionalTitle(title));
            foreach (var title in new[] { "update stuff", "feat: ", "fix: good\nmalicious", "Fix: wrong type" }) Check.True(!Readiness.ConventionalTitle(title));
        });
        foreach (var duplicate in new[] { false, true })
            yield return new($"GitHub readiness retains shared-head protection, shared={duplicate}", async () =>
            {
                var statuses = new List<JsonNode>(); var pull = Pull(1, new string('a', 40));
                var handler = new Handler(async request =>
                {
                    if (request.Method == HttpMethod.Post)
                    { statuses.Add(JsonNode.Parse(await request.Content!.ReadAsStringAsync())!); return Response("{}"); }
                    if (request.RequestUri!.AbsolutePath.Contains("/commits/", StringComparison.Ordinal))
                        return Response(new JsonArray(pull.DeepClone(), duplicate ? Pull(2, new string('a', 40)) : null).Where(item => item is not null).ToArray());
                    return Response(pull);
                });
                using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.invalid/") };
                var verdict = await new GitHubReadiness(http).CheckAsync("owner/repo", 1, Template, "https://example.invalid/run");
                Check.Equal(duplicate ? "failure" : "success", verdict!.State); Check.Equal(2, statuses.Count);
                Check.Equal("pending", statuses[0]["state"]!.GetValue<string>());
                Check.Equal("PR readiness", statuses[1]["context"]!.GetValue<string>());
            });
        yield return new("GitHub readiness rejects metadata races without certifying the new head", async () =>
        {
            var reads = 0; var first = Pull(1, new string('a', 40)); var latest = Pull(1, new string('b', 40));
            var handler = new Handler(request => Task.FromResult(request.Method == HttpMethod.Post ? Response("{}")
                : request.RequestUri!.AbsolutePath.Contains("/commits/", StringComparison.Ordinal) ? Response(new JsonArray(first.DeepClone()))
                : Response(++reads == 1 ? first : latest)));
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.invalid/") };
            Check.Equal("pending", (await new GitHubReadiness(http).CheckAsync("owner/repo", 1, Template, "https://example.invalid/run"))!.State);
        });
        yield return new("Registry exact-version lookup preserves absent and identical-active meanings", async () =>
        {
            var server = JsonNode.Parse("{\"name\":\"org/tool\",\"version\":\"1.0.0\"}")!.AsObject();
            using var absent = new HttpClient(new Handler(_ => Task.FromResult(Response("{\"servers\":[],\"metadata\":{}}")))) { BaseAddress = new Uri("https://example.invalid/") };
            Check.Equal("absent", await RegistryState.LookupAsync(absent, server));
            var entry = new JsonObject { ["server"] = server.DeepClone(), ["_meta"] = new JsonObject { ["io.modelcontextprotocol.registry/official"] = new JsonObject { ["status"] = "active" } } };
            using var present = new HttpClient(new Handler(_ => Task.FromResult(Response(new JsonObject { ["servers"] = new JsonArray(entry.DeepClone()), ["metadata"] = new JsonObject() })))) { BaseAddress = new Uri("https://example.invalid/") };
            Check.Equal("present", await RegistryState.LookupAsync(present, server));
        });
        yield return new("Registry cyclic cursors fail instead of treating unknown state as absent", async () =>
        {
            using var http = new HttpClient(new Handler(_ => Task.FromResult(Response("{\"servers\":[],\"metadata\":{\"nextCursor\":\"repeat\"}}")))) { BaseAddress = new Uri("https://example.invalid/") };
            var failed = false;
            try { await RegistryState.LookupAsync(http, JsonNode.Parse("{\"name\":\"org/tool\",\"version\":\"1\"}")!.AsObject()); }
            catch (InvalidDataException) { failed = true; }
            Check.True(failed);
        });
        yield return new("Registry explicit null cursor is malformed rather than an absent continuation", async () =>
        {
            using var http = new HttpClient(new Handler(_ => Task.FromResult(Response("{\"servers\":[],\"metadata\":{\"nextCursor\":null}}")))) { BaseAddress = new Uri("https://example.invalid/") };
            var failed = false;
            try { await RegistryState.LookupAsync(http, JsonNode.Parse("{\"name\":\"org/tool\",\"version\":\"1\"}")!.AsObject()); }
            catch (InvalidDataException) { failed = true; }
            Check.True(failed);
        });
        yield return new("Malformed PR metadata cannot be interpreted as a harmless closed PR", async () =>
        {
            using var http = new HttpClient(new Handler(_ => Task.FromResult(Response("{\"state\":\"closed\"}")))) { BaseAddress = new Uri("https://example.invalid/") };
            var failed = false;
            try { await new GitHubReadiness(http).CheckAsync("owner/repo", 1, Template, "https://example.invalid/run"); }
            catch (InvalidDataException) { failed = true; }
            Check.True(failed);
        });
        yield return Case.Sync("Documentation rendering escapes executable HTML and unsafe link schemes", () =>
        {
            var rendered = DocumentationBuilder.Render("# Title\n<script>alert(1)</script>\n[x](javascript:evil)\n```\n<script>code</script>\n```\n[guide](guide.md)");
            Check.True(rendered.Contains("<h1 id=\"title\">Title</h1>", StringComparison.Ordinal));
            Check.True(!rendered.Contains("<script>", StringComparison.Ordinal));
            Check.True(!rendered.Contains("href=\"javascript:", StringComparison.Ordinal));
            Check.True(rendered.Contains("guide.html", StringComparison.Ordinal));
        });
        yield return Case.Sync("Qualified .NET package digest and exact source cannot be substituted", () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "rea-qualification-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try
            {
                var path = Path.Combine(root, "fixture.nupkg");
                using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
                {
                    foreach (var file in new[] { "rea.dll", "Rea.Domain.dll", "Rea.Application.dll", "Rea.Managed.dll", "rea.runtimeconfig.json" }) zip.CreateEntry("tools/net10.0/any/" + file);
                    using var writer = new StreamWriter(zip.CreateEntry("fixture.nuspec").Open());
                    writer.Write("<package><metadata><id>Rea.DotNet.Preview</id><version>0.1.0</version></metadata></package>");
                }
                var sha = new string('a', 40); var manifest = Path.Combine(root, "manifest.json");
                File.WriteAllText(manifest, System.Text.Json.JsonSerializer.Serialize(PackageQualification.Inspect(path, sha)));
                PackageQualification.Verify(path, manifest, sha);
                var failed = false; try { PackageQualification.Verify(path, manifest, new string('b', 40)); } catch (InvalidDataException) { failed = true; }
                Check.True(failed);
            }
            finally { Directory.Delete(root, true); }
        });
        yield return new("Unavailable provider lanes fail explicitly rather than passing a no-op", async () =>
        { using var output = new StringWriter(); using var error = new StringWriter(); Check.Equal(78, await AutomationCommand.RunAsync(["unavailable", "ghidra"], output, error)); Check.True(error.ToString().Contains("unavailable", StringComparison.Ordinal)); });
    }

    private static JsonObject Pull(int number, string sha) => new()
    { ["number"] = number, ["state"] = "open", ["draft"] = false, ["body"] = Body, ["head"] = new JsonObject { ["sha"] = sha }, ["base"] = new JsonObject { ["ref"] = "main" } };
    private static HttpResponseMessage Response(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Response(JsonNode node) => Response(node.ToJsonString());
    private static HttpResponseMessage Response(JsonNode?[] nodes) => Response(new JsonArray(nodes.Select(node => node?.DeepClone()).ToArray()));
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> run) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => run(request); }
}
