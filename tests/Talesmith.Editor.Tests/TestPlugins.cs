using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Talesmith.Scripting.Compiler;

namespace Talesmith.Editor.Tests;

/// <summary>Compiles small plugins into a project's plugins folder.</summary>
internal static class TestPlugins
{
    public static void Write(string projectFolder, string id, string name)
    {
        var folder = Path.Combine(projectFolder, "assets", "plugins", id);
        var assemblyName = AssemblyName(id);
        Compile(assemblyName, RuntimeSource, ScriptReferences.Default(), folder);
        WriteManifest(folder, Manifest(id, name, assemblyName));
    }

    /// <summary>Writes a plugin with an editor assembly compiled from <paramref name="editorSource"/>, which has every assembly the tests
    /// load as references.</summary>
    public static void WriteWithEditor(string projectFolder, string id, string name, string editorSource)
    {
        var folder = Path.Combine(projectFolder, "assets", "plugins", id);
        var assemblyName = AssemblyName(id);
        Compile(assemblyName, RuntimeSource, ScriptReferences.Default(), folder);
        _ = typeof(Editor.Plugins.IEditorPlugin);
        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && File.Exists(a.Location) && a.GetName().Name?.StartsWith("Fixture.", StringComparison.Ordinal) == false)
            .GroupBy(a => a.GetName().Name)
            .Select(g => g.First().Location);
        Compile(assemblyName + ".Editor", editorSource, loaded, folder);
        var manifest = Manifest(id, name, assemblyName);
        manifest["editorAssembly"] = assemblyName + ".Editor.dll";
        manifest["permissions"] = new JsonArray("runtimeScene", "editorUi");
        WriteManifest(folder, manifest);
    }

    private const string RuntimeSource = """
        public sealed class FixturePlugin : Talesmith.Plugins.IPlugin
        {
            public void Configure(Talesmith.Plugins.IPluginBuilder builder) => builder.Settings.Get("speed", 1);
        }
        """;

    private static string AssemblyName(string id) => "Fixture." + id.Replace('-', '_');

    private static void Compile(string assemblyName, string source, IEnumerable<string> references, string folder)
    {
        Directory.CreateDirectory(folder);
        var compilation = CSharpCompilation.Create(assemblyName, [CSharpSyntaxTree.ParseText(source)],
            references.Distinct(StringComparer.Ordinal).Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var result = compilation.Emit(Path.Combine(folder, assemblyName + ".dll"));
        if (!result.Success)
            throw new InvalidOperationException(string.Join("\n", result.Diagnostics));
    }

    private static JsonObject Manifest(string id, string name, string assemblyName) => new()
    {
        ["id"] = id,
        ["name"] = name,
        ["version"] = "1.2.0",
        ["description"] = "A plugin compiled by the tests.",
        ["authors"] = new JsonArray("Tests"),
        ["assembly"] = assemblyName + ".dll",
        ["contractVersion"] = EngineInfo.ContractVersion,
        ["permissions"] = new JsonArray("runtimeScene"),
        ["extensions"] = new JsonArray("systems")
    };

    private static void WriteManifest(string folder, JsonObject manifest) =>
        File.WriteAllText(Path.Combine(folder, "plugin.json"), manifest.ToJsonString());
}
