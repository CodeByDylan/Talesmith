using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Talesmith.Editor.Projects;
using Talesmith.Plugins;
using Talesmith.Scripting.Compiler;

namespace Talesmith.Editor.Plugins;

/// <summary>The files of a new plugin project.</summary>
public sealed record CreatedPlugin(string Folder, string ProjectFile, string Manifest, string PluginClass);

/// <summary>Creates plugin projects in <c>&lt;project&gt;/plugins-src/</c>: a class library with <c>plugin.json</c> and an <c>IPlugin</c> class that
/// installs itself into the game's plugins folder when built.</summary>
/// <remarks>The project references the engine assemblies next to the editor without copying them, as plugins must.</remarks>
public sealed class PluginScaffold(EditorProject project)
{
    public const string SourceFolder = "plugins-src";

    /// <summary>Packages of native libraries the engine ships for its shared assemblies.</summary>
    private static readonly string[] EngineNativePackages = ["HarfBuzzSharp", "Silk.NET"];

    /// <summary>Matches assembly and package names plugins get from the host, so installing a plugin leaves them out.</summary>
    private static readonly string HostSharedPattern =
        $"^({string.Join('|', PluginLoadContext.DefaultSharedAssemblies.Concat(EngineNativePackages).Select(Regex.Escape))})(\\.|$)";

    public async Task<CreatedPlugin> CreateAsync(string displayName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        var name = ScriptTemplates.NamespaceFor(displayName.Trim());
        if (ScriptTemplates.ValidateClassName(name) is { } error)
            throw new ArgumentException(error, nameof(displayName));
        var folderName = Slug(displayName);
        var folder = Path.Combine(project.Folder, SourceFolder, name);
        if (Directory.Exists(folder))
            throw new IOException($"{Path.GetRelativePath(project.Folder, folder)} already exists.");
        Directory.CreateDirectory(folder);

        var id = $"{Slug(project.Name)}.{folderName}".Trim('.');
        var manifest = new JsonObject
        {
            ["id"] = id,
            ["name"] = displayName.Trim(),
            ["version"] = "1.0.0",
            ["description"] = "",
            ["authors"] = new JsonArray(Environment.UserName),
            ["assembly"] = name + ".dll",
            ["contractVersion"] = EngineInfo.ContractVersion,
            ["permissions"] = new JsonArray("runtimeScene"),
            ["extensions"] = new JsonArray("systems")
        };
        var manifestPath = Path.Combine(folder, "plugin.json");
        await File.WriteAllTextAsync(manifestPath, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n", cancellationToken);

        var projectFile = Path.Combine(folder, name + ".csproj");
        await File.WriteAllTextAsync(projectFile, ProjectFile(folderName), new UTF8Encoding(false), cancellationToken);

        var pluginClass = Path.Combine(folder, name + "Plugin.cs");
        await File.WriteAllTextAsync(pluginClass, PluginSource(name), new UTF8Encoding(false), cancellationToken);
        return new CreatedPlugin(folder, projectFile, manifestPath, pluginClass);
    }

    private string ProjectFile(string folderName)
    {
        XElement Property(string key, string value) => new(key, value);
        var root = new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new XElement("PropertyGroup",
                Property("TargetFramework", $"net{Environment.Version.Major}.{Environment.Version.Minor}"),
                Property("Nullable", "enable"),
                Property("ImplicitUsings", "enable"),
                Property("EnableDynamicLoading", "true"),
                Property("ImportDirectoryBuildProps", "false"),
                Property("ManagePackageVersionsCentrally", "false"),
                Property("PluginDestination", $"$(MSBuildThisFileDirectory)../../assets/{GameSettingsFolder()}/{folderName}/")),
            new XElement("ItemGroup", ScriptReferences.Engine().Select(path => new XElement("Reference",
                new XAttribute("Include", Path.GetFileNameWithoutExtension(path)),
                Property("HintPath", path),
                Property("Private", "false")))),
            new XElement("ItemGroup", new XElement("None", new XAttribute("Include", "plugin.json"), new XAttribute("CopyToOutputDirectory", "PreserveNewest"))),
            new XComment(" Copies the plugin and its private dependencies, such as NuGet packages, but not what the editor and games share with plugins. "),
            new XElement("Target", new XAttribute("Name", "InstallPlugin"), new XAttribute("AfterTargets", "Build"),
                new XElement("PropertyGroup", Property("HostSharedPattern", HostSharedPattern)),
                new XElement("ItemGroup",
                    new XElement("PluginFiles",
                        new XAttribute("Include", "$(TargetDir)$(TargetName).dll;$(TargetDir)$(TargetName).pdb;$(TargetDir)$(TargetName).deps.json;$(TargetDir)plugin.json")),
                    new XElement("PluginDependencies", new XAttribute("Include", "@(ReferenceCopyLocalPaths)"), new XAttribute("Condition",
                        "!$([System.Text.RegularExpressions.Regex]::IsMatch('%(ReferenceCopyLocalPaths.Filename)', '$(HostSharedPattern)')) And "
                        + "!$([System.Text.RegularExpressions.Regex]::IsMatch('%(ReferenceCopyLocalPaths.NuGetPackageId)', '$(HostSharedPattern)'))"))),
                new XElement("Copy", new XAttribute("SourceFiles", "@(PluginFiles)"), new XAttribute("DestinationFolder", "$(PluginDestination)"),
                    new XAttribute("SkipUnchangedFiles", "true")),
                new XElement("Copy", new XAttribute("SourceFiles", "@(PluginDependencies)"),
                    new XAttribute("DestinationFiles", "@(PluginDependencies->'$(PluginDestination)%(DestinationSubDirectory)%(Filename)%(Extension)')"),
                    new XAttribute("SkipUnchangedFiles", "true"))));
        return root.ToString().ReplaceLineEndings("\n") + "\n";
    }

    private string GameSettingsFolder()
    {
        try
        {
            return Runtime.Hosting.GameSettings.Load(project.AssetRoot).PluginsFolder;
        }
        catch (InvalidDataException)
        {
            return "plugins";
        }
    }

    private static string PluginSource(string name) => $$"""
        using Microsoft.Extensions.DependencyInjection;
        using Talesmith.Ecs;
        using Talesmith.Plugins;
        using Talesmith.Systems;

        namespace {{name}};

        public sealed class {{name}}Plugin : IPlugin
        {
            public void Configure(IPluginBuilder builder)
            {
                builder.Services.AddSystem<{{name}}System>();
            }
        }

        [UpdateIn(SystemPhase.Update)]
        public sealed class {{name}}System : ISystem
        {
            public void Update(in SystemContext context)
            {
            }
        }

        """.ReplaceLineEndings("\n");

    private static string Slug(string text)
    {
        var builder = new StringBuilder();
        foreach (var c in text.Trim().ToLower(CultureInfo.InvariantCulture))
        {
            if (char.IsAsciiLetterOrDigit(c))
                builder.Append(c);
            else if (builder.Length > 0 && builder[^1] != '-')
                builder.Append('-');
        }

        return builder.ToString().Trim('-') is { Length: > 0 } slug ? slug : "plugin";
    }
}
