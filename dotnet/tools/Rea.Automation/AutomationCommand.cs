using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rea.Automation;

public static class AutomationCommand
{
    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error)
    {
        try
        {
            if (args.Length == 0) throw new ArgumentException("Commands: title, readiness, readiness-github, registry-state, docs, qualify-package, verify-package, verify-report, unavailable");
            switch (args[0])
            {
                case "title" when args.Length is 1 or 2:
                    if (!Readiness.ConventionalTitle(args.Length == 2 ? args[1] : Required("PR_TITLE")))
                        throw new InvalidDataException("PR title must use Conventional Commits, e.g. feat: add a C# adapter");
                    await output.WriteLineAsync("Conventional PR title verified."); return 0;
                case "readiness" when args.Length is 3 or 4:
                    var verdict = Readiness.Evaluate(args.Length == 4 && args[3] == "draft", File.ReadAllText(args[1]), File.ReadAllText(args[2]));
                    await output.WriteLineAsync(JsonSerializer.Serialize(verdict)); return verdict.State == "failure" ? 1 : 0;
                case "readiness-github" when args.Length == 1:
                    var payload = JsonNode.Parse(File.ReadAllText(Required("GITHUB_EVENT_PATH"))) ?? throw new InvalidDataException("Missing event");
                    var number = payload["pull_request"]?["number"]?.GetValue<int>()
                        ?? int.Parse(payload["inputs"]?["pull_request_number"]?.GetValue<string>() ?? throw new InvalidDataException("PR number required"), System.Globalization.CultureInfo.InvariantCulture);
                    using (var http = GitHubReadiness.CreateClient(Required("GITHUB_API_URL"), Required("GITHUB_TOKEN")))
                    {
                        var repository = Required("GITHUB_REPOSITORY");
                        var url = Required("GITHUB_SERVER_URL").TrimEnd('/') + "/" + repository + "/actions/runs/" + Required("GITHUB_RUN_ID");
                        var decision = await new GitHubReadiness(http).CheckAsync(repository, number, File.ReadAllText(".github/pull_request_template.md"), url);
                        await output.WriteLineAsync(JsonSerializer.Serialize(decision)); return decision?.State == "failure" ? 1 : 0;
                    }
                case "registry-state" when args.Length == 2:
                    var server = JsonNode.Parse(File.ReadAllText(args[1])) as JsonObject ?? throw new InvalidDataException("Server metadata must be an object");
                    using (var http = new HttpClient { BaseAddress = new Uri("https://registry.modelcontextprotocol.io/"), Timeout = TimeSpan.FromSeconds(30) })
                    { await output.WriteLineAsync(await RegistryState.LookupAsync(http, server)); return 0; }
                case "docs" when args.Length == 3:
                    await output.WriteLineAsync(JsonSerializer.Serialize(new { pages = DocumentationBuilder.Build(args[1], args[2]), output = Path.GetFullPath(args[2]) })); return 0;
                case "qualify-package" when args.Length == 4:
                    await SourceQualification.RequireCleanCheckoutAsync(args[2]);
                    var package = PackageQualification.Inspect(args[1], args[2]);
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[3]))!);
                    await File.WriteAllTextAsync(args[3], JsonSerializer.Serialize(package, new JsonSerializerOptions { WriteIndented = true }));
                    await output.WriteLineAsync(JsonSerializer.Serialize(package)); return 0;
                case "verify-package" when args.Length == 4:
                    PackageQualification.Verify(args[1], args[2], args[3]); await output.WriteLineAsync("Qualified package verified."); return 0;
                case "qualify-package-env" when args.Length == 1:
                    await SourceQualification.RequireCleanCheckoutAsync(Required("REA_SOURCE_SHA"));
                    var qualified = PackageQualification.Inspect(Required("REA_PACKAGE_PATH"), Required("REA_SOURCE_SHA"));
                    var qualifiedManifest = Required("REA_PACKAGE_MANIFEST");
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(qualifiedManifest))!);
                    await File.WriteAllTextAsync(qualifiedManifest, JsonSerializer.Serialize(qualified, new JsonSerializerOptions { WriteIndented = true }));
                    await output.WriteLineAsync(JsonSerializer.Serialize(qualified)); return 0;
                case "verify-package-env" when args.Length == 1:
                    PackageQualification.Verify(Required("REA_PACKAGE_PATH"), Required("REA_PACKAGE_MANIFEST"), Required("REA_SOURCE_SHA"));
                    await output.WriteLineAsync("Qualified package verified."); return 0;
                case "qualify-directory-env" when args.Length == 1:
                    await SourceQualification.RequireCleanCheckoutAsync(Required("REA_SOURCE_SHA"));
                    var packageDirectory = Required("REA_PACKAGE_DIRECTORY");
                    var selectedPackage = Directory.GetFiles(packageDirectory, "*.nupkg").Single();
                    var receipt = PackageQualification.Inspect(selectedPackage, Required("REA_SOURCE_SHA"));
                    await File.WriteAllTextAsync(Path.Combine(packageDirectory, "package-manifest.json"), JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
                    await output.WriteLineAsync(JsonSerializer.Serialize(receipt)); return 0;
                case "verify-directory-env" when args.Length == 1:
                    var qualifiedDirectory = Required("REA_PACKAGE_DIRECTORY");
                    var receiptPath = Path.Combine(qualifiedDirectory, "package-manifest.json");
                    var selectedReceipt = JsonSerializer.Deserialize<QualifiedPackage>(await File.ReadAllTextAsync(receiptPath)) ?? throw new InvalidDataException("Package receipt missing");
                    if (selectedReceipt.FileName != Path.GetFileName(selectedReceipt.FileName)) throw new InvalidDataException("Package receipt filename is not a basename");
                    PackageQualification.Verify(Path.Combine(qualifiedDirectory, selectedReceipt.FileName), receiptPath, Required("REA_SOURCE_SHA"));
                    await output.WriteLineAsync("Qualified package directory verified."); return 0;
                case "verify-report" when args.Length == 2:
                    var report = JsonNode.Parse(File.ReadAllText(args[1])) ?? throw new InvalidDataException("Missing test report");
                    var cases = report["cases"] as JsonArray ?? throw new InvalidDataException("Test report omitted cases");
                    if (cases.Count == 0 || report["failures"]?.GetValue<int>() != 0 || cases.Any(item => item?["passed"]?.GetValue<bool>() != true))
                        throw new InvalidDataException("Required tests failed or no tests ran");
                    await output.WriteLineAsync($"Verified {cases.Count} executed test cases."); return 0;
                case "unavailable" when args.Length >= 2:
                    await error.WriteLineAsync(JsonSerializer.Serialize(new { state = "unavailable", operation = args[1], reason = "This historical lane requires a C# provider or publication contract not implemented in the current checkout. No provider verification or publication was performed." }));
                    return 78;
                default: throw new ArgumentException("Invalid automation command or arguments");
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or IOException or JsonException or HttpRequestException or InvalidOperationException or FormatException or OperationCanceledException or System.Xml.XmlException or System.ComponentModel.Win32Exception)
        { await error.WriteLineAsync(ex.Message); return 1; }
    }
    private static string Required(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : throw new ArgumentException("Required input: " + name);
}
