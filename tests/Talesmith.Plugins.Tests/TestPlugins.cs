using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Talesmith.Plugins.Tests;

/// <summary>A temporary game folder with a plugins folder and a configuration file, into which tests install plugins.</summary>
internal sealed class TestPlugins : IDisposable
{
    private static readonly ConcurrentDictionary<string, byte[]> Compiled = new();

    private static readonly Lazy<MetadataReference[]> HostReferences = new(() =>
        [.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Select(p => MetadataReference.CreateFromFile(p))]);

    public TestPlugins()
    {
        Root = Path.Combine(Path.GetTempPath(), "talesmith-plugin-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(PluginsDirectory);
    }

    public string Root { get; }

    public string PluginsDirectory => Path.Combine(Root, "plugins");

    public string ConfigurationFile => Path.Combine(Root, "config", "plugins.json");

    public PluginLoadOptions Options(PluginPermissionPolicy policy = PluginPermissionPolicy.Warn, bool editor = false) => new()
    {
        PluginsDirectory = PluginsDirectory,
        ConfigurationFile = ConfigurationFile,
        Collectible = true,
        PermissionPolicy = policy,
        LoadEditorAssemblies = editor
    };

    /// <summary>The source of a plugin whose <c>Configure</c> runs <paramref name="configure"/>, plus any extra types.</summary>
    public static string PluginSource(string ns, string configure, string extraTypes = "") => $$"""
        using Microsoft.Extensions.DependencyInjection;
        using Talesmith.Plugins;

        namespace {{ns}};

        public sealed class EntryPlugin : IPlugin
        {
            public void Configure(IPluginBuilder builder)
            {
                var services = builder.Services;
                {{configure}}
            }
        }

        {{extraTypes}}
        """;

    /// <summary>A manifest for <paramref name="id"/> with an assembly named after it and any extra JSON properties.</summary>
    public static string Manifest(string id, string extra = "") =>
        $$"""{ "id": "{{id}}", "version": "1.0.0", "assembly": "{{AssemblyName(id)}}.dll", "contractVersion": 1{{(extra.Length > 0 ? ", " + extra : "")}} }""";

    public static string AssemblyName(string id) => "Plugin." + string.Concat(id.Split('.', '-', '_').Select(p => char.ToUpperInvariant(p[0]) + p[1..]));

    /// <summary>Writes a plugin folder with a manifest and nothing else.</summary>
    public string AddManifest(string folder, string manifest)
    {
        var directory = Path.Combine(PluginsDirectory, folder);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, PluginManifest.FileName), manifest);
        return directory;
    }

    /// <summary>Compiles <paramref name="source"/> into the plugin's assembly and writes its manifest.</summary>
    public byte[] Add(string id, string source, string manifestExtra = "", params byte[][] references)
    {
        var directory = AddManifest(id, Manifest(id, manifestExtra));
        var image = Compile(AssemblyName(id), source, references);
        File.WriteAllBytes(Path.Combine(directory, AssemblyName(id) + ".dll"), image);
        return image;
    }

    /// <summary>Compiles an editor assembly into an installed plugin's folder.</summary>
    public void AddEditorAssembly(string id, string fileName, string source, params byte[][] references)
    {
        var image = Compile(Path.GetFileNameWithoutExtension(fileName), source, references);
        File.WriteAllBytes(Path.Combine(PluginsDirectory, id, fileName), image);
    }

    public static byte[] Compile(string assemblyName, string source, params byte[][] references) =>
        Compiled.GetOrAdd($"{assemblyName}\n{source}\n{string.Join(",", references.Select(r => r.Length))}", _ =>
        {
            var compilation = CSharpCompilation.Create(
                assemblyName,
                [CSharpSyntaxTree.ParseText(source)],
                [.. HostReferences.Value, .. references.Select(r => MetadataReference.CreateFromImage(r))],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
            using var output = new MemoryStream();
            var result = compilation.Emit(output);
            if (!result.Success)
                throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
            return output.ToArray();
        });

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
