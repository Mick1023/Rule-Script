using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RuleScript.Core;
using RuleScript.Core.Runtime;
using RuleScript.LanguageServer;

namespace RuleScript.LanguageServer.Tests;

public sealed class RuleScriptWorkspaceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"rulescript-workspace-{Guid.NewGuid():N}");

    public RuleScriptWorkspaceTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Imports_PreferUnsavedOpenBuffersOverDisk()
    {
        File.WriteAllText(Path.Combine(_root, "lib.rules"), "export function OldName():\n    return 1;\nendfunction");
        var workspace = CreateWorkspace(_root);
        var lib = Uri("lib.rules");
        var main = Uri("main.rules");

        workspace.Update(lib, "export function NewName():\n    return 2;\nendfunction", 1);
        workspace.Update(main, "import \"lib\" as lib;\nvar value = lib.", 1);

        var labels = RuleScriptLanguageService.GetCompletions(workspace.GetDocument(main)!, 2, 17).Items.Select(item => item.Label);
        Assert.Equal(["NewName"], labels);
    }

    [Fact]
    public void ChangingAnImportedBuffer_RefreshesImporterAnalysis()
    {
        var workspace = CreateWorkspace(_root);
        var lib = Uri("lib.rules");
        var main = Uri("main.rules");
        workspace.Update(lib, "export function First():\n    return 1;\nendfunction", 1);
        workspace.Update(main, "import \"lib\" as lib;\nvar value = lib.", 1);
        Assert.Contains(GetCompletionLabels(workspace, main, 2, 17), label => label == "First");

        workspace.Update(lib, "export function Second():\n    return 2;\nendfunction", 2);

        Assert.Equal(["Second"], GetCompletionLabels(workspace, main, 2, 17));
    }

    [Fact]
    public void Manifest_DeclaresHostFunctionsAndVariablesForAnalysis()
    {
        File.WriteAllText(Path.Combine(_root, "rulescript.json"), """
            {
              // Comments and trailing commas are allowed.
              "hostFunctions": [
                {
                  "name": "GetDistance",
                  "parameters": [{ "name": "sensorId", "type": "string" }],
                  "returnType": "number",
                  "documentation": "Reads a distance sensor."
                },
              ],
              "variables": [{ "name": "Limit", "type": "number" }]
            }
            """);
        var workspace = CreateWorkspace(_root);
        var main = Uri("main.rules");

        workspace.Update(main, "var distance = GetDistance(\"S1\");\nif distance > Limit then:\n    result = \"NG\";\nendif", 1);
        var document = workspace.GetDocument(main)!;

        Assert.DoesNotContain(document.Diagnostics, diagnostic =>
            diagnostic.Code is RuleScriptDiagnosticCodes.UndefinedFunction or RuleScriptDiagnosticCodes.UndefinedVariable);
        Assert.Equal(
            "host function GetDistance(sensorId: string) -> number",
            RuleScriptLanguageService.GetHover(document, 1, 16)?.Signature);
        Assert.Equal("Reads a distance sensor.", RuleScriptLanguageService.GetHover(document, 1, 16)?.Documentation);
        Assert.Contains(GetCompletionLabels(workspace, main, 1, 1), label => label == "GetDistance");
    }

    [Fact]
    public void WithoutManifest_HostSymbolsAreReportedAsUndefined()
    {
        var workspace = CreateWorkspace(_root);
        var main = Uri("main.rules");

        workspace.Update(main, "var distance = GetDistance(\"S1\") + Limit;", 1);

        var codes = workspace.GetDocument(main)!.Diagnostics.Select(diagnostic => diagnostic.Code).ToArray();
        Assert.Contains(RuleScriptDiagnosticCodes.UndefinedFunction, codes);
        Assert.Contains(RuleScriptDiagnosticCodes.UndefinedVariable, codes);
    }

    [Fact]
    public void Manifest_ReportsInvalidEntriesAndMalformedJson()
    {
        File.WriteAllText(Path.Combine(_root, "rulescript.json"), """{ "hostFunctions": [{ "name": "Bad", "returnType": "decimal" }, {}] }""");
        var warnings = new List<string>();

        CreateWorkspace(_root, warnings.Add);

        Assert.Contains(warnings, warning => warning.Contains("Unknown type 'decimal'", StringComparison.Ordinal));
        Assert.Contains(warnings, warning => warning.Contains("missing 'name'", StringComparison.Ordinal));

        File.WriteAllText(Path.Combine(_root, "rulescript.json"), "{ not json");
        warnings.Clear();
        CreateWorkspace(_root, warnings.Add);
        Assert.Contains(warnings, warning => warning.Contains("could not be read", StringComparison.Ordinal));
    }

    [Fact]
    public void ReloadManifest_AppliesChangedDeclarations()
    {
        var workspace = CreateWorkspace(_root);
        var main = Uri("main.rules");
        workspace.Update(main, "var value = Limit;", 1);
        Assert.Contains(workspace.GetDocument(main)!.Diagnostics, diagnostic => diagnostic.Code == RuleScriptDiagnosticCodes.UndefinedVariable);

        var manifest = Path.Combine(_root, "rulescript.json");
        File.WriteAllText(manifest, """{ "variables": [{ "name": "Limit", "type": "number" }] }""");

        Assert.True(workspace.ReloadManifest(manifest));
        Assert.DoesNotContain(workspace.GetDocument(main)!.Diagnostics, diagnostic => diagnostic.Code == RuleScriptDiagnosticCodes.UndefinedVariable);
        Assert.False(workspace.ReloadManifest(Path.Combine(Path.GetTempPath(), "elsewhere", "rulescript.json")));
    }

    [Fact]
    public void WorkspaceFolders_UseTheirOwnManifestsAndNestedFoldersWin()
    {
        var plant = Path.Combine(_root, "plant");
        var line = Path.Combine(plant, "line");
        Directory.CreateDirectory(line);
        File.WriteAllText(Path.Combine(plant, "rulescript.json"), """{ "variables": [{ "name": "PlantId", "type": "string" }] }""");
        File.WriteAllText(Path.Combine(line, "rulescript.json"), """{ "variables": [{ "name": "LineId", "type": "string" }] }""");
        var workspace = new RuleScriptWorkspace();
        workspace.Initialize([DocumentUri.FromFileSystemPath(plant), DocumentUri.FromFileSystemPath(line)]);

        var plantDocument = DocumentUri.FromFileSystemPath(Path.Combine(plant, "main.rules"));
        var lineDocument = DocumentUri.FromFileSystemPath(Path.Combine(line, "main.rules"));
        workspace.Update(plantDocument, "var a = PlantId;\nvar b = LineId;", 1);
        workspace.Update(lineDocument, "var a = PlantId;\nvar b = LineId;", 1);

        Assert.Equal([2], UndefinedVariableLines(workspace, plantDocument));
        Assert.Equal([1], UndefinedVariableLines(workspace, lineDocument));
        Assert.Equal(Path.GetFullPath(line), workspace.GetEngine(lineDocument).WorkingDirectory);
    }

    [Fact]
    public void Rename_MapsEditsAndRejectsWithReason()
    {
        var uri = DocumentUri.From("untitled:main.rules");
        var document = RuleScriptLanguageService.TryAnalyzeDocument("var count = 1;\ncount = count + Length(\"x\");");

        var edit = RuleScriptLanguageFeatures.Rename(document, uri, new Position(0, 5), "total");
        var placeholder = RuleScriptLanguageFeatures.PrepareRename(document, new Position(1, 9));

        var edits = Assert.Single(edit.Changes!).Value.ToArray();
        Assert.Equal(3, edits.Length);
        Assert.All(edits, textEdit => Assert.Equal("total", textEdit.NewText));
        Assert.Equal("count", placeholder.PlaceholderRange!.Placeholder);

        var rejected = Assert.Throws<RenameRejectedException>(() =>
            RuleScriptLanguageFeatures.Rename(document, uri, new Position(1, 17), "Size"));
        Assert.Contains("Host Functions", rejected.Message);
    }

    private RuleScriptWorkspace CreateWorkspace(string root, Action<string>? warn = null)
    {
        var workspace = new RuleScriptWorkspace { Warn = warn };
        workspace.Initialize([DocumentUri.FromFileSystemPath(root)]);
        return workspace;
    }

    private DocumentUri Uri(string fileName) => DocumentUri.FromFileSystemPath(Path.Combine(_root, fileName));

    private static IEnumerable<string> GetCompletionLabels(RuleScriptWorkspace workspace, DocumentUri uri, int line, int column)
    {
        return RuleScriptLanguageService.GetCompletions(workspace.GetDocument(uri)!, line, column).Items.Select(item => item.Label);
    }

    private static int[] UndefinedVariableLines(RuleScriptWorkspace workspace, DocumentUri uri)
    {
        return workspace.GetDocument(uri)!.Diagnostics
            .Where(diagnostic => diagnostic.Code == RuleScriptDiagnosticCodes.UndefinedVariable)
            .Select(diagnostic => diagnostic.Line ?? 0)
            .ToArray();
    }
}
