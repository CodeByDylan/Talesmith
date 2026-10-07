using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Talesmith.Plugins;

/// <summary>What a plugin assembly's metadata says about the permissions it needs.</summary>
/// <param name="Evidence">For each permission, the referenced types that need it.</param>
/// <param name="EditorReferences">Editor-only engine assemblies the assembly references.</param>
internal sealed record PluginCodeFindings(IReadOnlyDictionary<PluginPermissions, IReadOnlyList<string>> Evidence, IReadOnlyList<string> EditorReferences)
{
    public static PluginCodeFindings None { get; } = new(new Dictionary<PluginPermissions, IReadOnlyList<string>>(), []);
}

/// <summary>Reads the type references of a plugin assembly, without loading it, to find permissions it uses.</summary>
/// <remarks>This sees direct use of well-known APIs only; reflection, private dependencies and engine services that do the work are invisible.</remarks>
internal static class PluginCodeInspector
{
    private static readonly string[] EditorAssemblies = ["Talesmith.Editor", "Talesmith.UI", "Talesmith.Build", "Talesmith.Scripting.Compiler", "Talesmith.App"];

    private static readonly (string Namespace, string? Name, PluginPermissions Permission)[] Rules =
    [
        ("System.IO", "File", PluginPermissions.FileSystem),
        ("System.IO", "FileInfo", PluginPermissions.FileSystem),
        ("System.IO", "Directory", PluginPermissions.FileSystem),
        ("System.IO", "DirectoryInfo", PluginPermissions.FileSystem),
        ("System.IO", "FileStream", PluginPermissions.FileSystem),
        ("System.IO", "FileSystemWatcher", PluginPermissions.FileSystem),
        ("System.IO", "DriveInfo", PluginPermissions.FileSystem),
        ("System.Net.Http", null, PluginPermissions.Network),
        ("System.Net.Sockets", null, PluginPermissions.Network),
        ("System.Net.WebSockets", null, PluginPermissions.Network),
        ("System.Net", "WebClient", PluginPermissions.Network),
        ("System.Net", "WebRequest", PluginPermissions.Network),
        ("System.Net", "HttpWebRequest", PluginPermissions.Network),
        ("System.Net", "HttpListener", PluginPermissions.Network),
        ("System.Net", "Dns", PluginPermissions.Network),
        ("System.Diagnostics", "Process", PluginPermissions.ProcessExecution),
        ("System.Diagnostics", "ProcessStartInfo", PluginPermissions.ProcessExecution),
        ("Talesmith.Editor", null, PluginPermissions.EditorUi),
        ("Talesmith.UI", null, PluginPermissions.EditorUi),
        ("Talesmith.Rendering.Vulkan", null, PluginPermissions.RenderBackend),
        ("Talesmith.Rendering.Skia", null, PluginPermissions.RenderBackend),
        ("Vortice.Vulkan", null, PluginPermissions.RenderBackend)
    ];

    /// <summary>Inspects an assembly file; files that are not readable .NET assemblies yield no findings and fail later when loaded.</summary>
    public static PluginCodeFindings Inspect(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var image = new PEReader(stream);
            return image.HasMetadata ? Inspect(image.GetMetadataReader()) : PluginCodeFindings.None;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException or InvalidOperationException)
        {
            return PluginCodeFindings.None;
        }
    }

    private static PluginCodeFindings Inspect(MetadataReader metadata)
    {
        var evidence = new Dictionary<PluginPermissions, SortedSet<string>>();
        foreach (var handle in metadata.TypeReferences)
        {
            var reference = metadata.GetTypeReference(handle);
            if (reference.ResolutionScope.Kind == HandleKind.TypeReference)
                continue;
            var ns = metadata.GetString(reference.Namespace);
            var name = metadata.GetString(reference.Name);
            foreach (var rule in Rules)
            {
                if (!Matches(rule.Namespace, rule.Name, ns, name))
                    continue;
                if (!evidence.TryGetValue(rule.Permission, out var types))
                    evidence[rule.Permission] = types = new SortedSet<string>(StringComparer.Ordinal);
                types.Add($"{ns}.{name}");
            }
        }

        var editorReferences = new List<string>();
        foreach (var handle in metadata.AssemblyReferences)
        {
            var name = metadata.GetString(metadata.GetAssemblyReference(handle).Name);
            if (Array.IndexOf(EditorAssemblies, name) >= 0)
                editorReferences.Add(name);
        }

        return new PluginCodeFindings(evidence.ToDictionary(p => p.Key, p => (IReadOnlyList<string>)[.. p.Value]), editorReferences);
    }

    private static bool Matches(string ruleNamespace, string? ruleName, string ns, string name)
    {
        if (ruleName is not null)
            return ns == ruleNamespace && name == ruleName;
        return ns.StartsWith(ruleNamespace, StringComparison.Ordinal) && (ns.Length == ruleNamespace.Length || ns[ruleNamespace.Length] == '.');
    }
}
