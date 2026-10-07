using System.Text;
using Microsoft.CodeAnalysis.CSharp;

namespace Talesmith.Scripting.Compiler;

/// <summary>What a new script file contains.</summary>
public enum ScriptTemplate
{
    /// <summary>A <see cref="Script"/> with <c>OnStart</c> and <c>Update</c>, for behavior attached to entities.</summary>
    Script,

    /// <summary>An ECS system that runs every frame over the entities of a query.</summary>
    System,

    /// <summary>An ECS component struct that scenes and the inspector can use.</summary>
    Component,

    /// <summary>An empty class.</summary>
    Class
}

/// <summary>Creates new script files from templates, such as for the editor's Create &gt; Script command.</summary>
public static class ScriptTemplates
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.Ordinal) { nameof(Scripting.Script), "System", "Talesmith" };

    /// <summary>Checks a class name for a new script; returns why it cannot be used, or null when it can.</summary>
    public static string? ValidateClassName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Enter a class name.";
        if (!SyntaxFacts.IsValidIdentifier(name) || name.StartsWith('@'))
            return $"'{name}' is not a valid C# class name: use letters, digits and underscores, starting with a letter.";
        if (SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None || SyntaxFacts.GetContextualKeywordKind(name) != SyntaxKind.None)
            return $"'{name}' is a C# keyword.";
        if (ReservedNames.Contains(name))
            return $"'{name}' is the name of an engine type or namespace; choose another.";
        if (!char.IsUpper(name[0]))
            return $"Class names start with a capital letter, such as '{char.ToUpperInvariant(name[0])}{name[1..]}'.";
        return null;
    }

    /// <summary>The namespace for a script: the project name as an identifier, followed by the folders between the scripts folder and the file.</summary>
    /// <param name="projectName">The project's name, such as "isle-hopper", which becomes "IsleHopper".</param>
    /// <param name="relativeFolder">The file's folder relative to the scripts folder, such as "Enemies/Bosses"; empty for the scripts folder itself.</param>
    public static string NamespaceFor(string projectName, string? relativeFolder = null)
    {
        ArgumentNullException.ThrowIfNull(projectName);
        var parts = new List<string> { Identifier(projectName, "Game") };
        if (!string.IsNullOrEmpty(relativeFolder))
        {
            parts.AddRange(relativeFolder.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
                .Where(segment => segment != ".")
                .Select(segment => Identifier(segment, "Scripts")));
        }

        return string.Join('.', parts);
    }

    /// <summary>The source of a new file.</summary>
    /// <exception cref="ArgumentException">The class name is not valid; see <see cref="ValidateClassName"/>.</exception>
    public static string Render(ScriptTemplate template, string className, string @namespace)
    {
        if (ValidateClassName(className) is { } error)
            throw new ArgumentException(error, nameof(className));
        ArgumentException.ThrowIfNullOrWhiteSpace(@namespace);
        var body = template switch
        {
            ScriptTemplate.Script => $$"""
                /// <summary>Describe what {{className}} does.</summary>
                public sealed class {{className}} : Script
                {
                    [Tooltip("How fast the entity moves, in units per second.")]
                    public float Speed = 100;

                    protected override void OnStart()
                    {
                    }

                    protected override void Update()
                    {
                    }
                }
                """,
            ScriptTemplate.System => $$"""
                /// <summary>Describe what {{className}} does.</summary>
                [UpdateIn(SystemPhase.Update)]
                public sealed class {{className}} : ISystem
                {
                    public void Update(in SystemContext context)
                    {
                        foreach (var archetype in context.World.Query<Transform>())
                        {
                            var transforms = archetype.GetSpan<Transform>();
                            for (var i = 0; i < transforms.Length; i++)
                            {
                            }
                        }
                    }
                }
                """,
            ScriptTemplate.Component => $$"""
                /// <summary>Describe the data {{className}} holds.</summary>
                [Component(Category = "Gameplay")]
                public struct {{className}}
                {
                    public float Value;
                }
                """,
            _ => $$"""
                public sealed class {{className}}
                {
                }
                """
        };
        return new StringBuilder().Append("namespace ").Append(@namespace).Append(";\n\n").Append(body.ReplaceLineEndings("\n")).Append('\n').ToString();
    }

    /// <summary>Writes a new script file and returns its full path.</summary>
    /// <param name="folder">The folder to create it in, inside the project's scripts folder.</param>
    /// <exception cref="ArgumentException">The class name is not valid or the folder is outside the scripts folder.</exception>
    /// <exception cref="IOException">A file of that name already exists.</exception>
    public static string Create(ScriptCompilerOptions options, string folder, string className, ScriptTemplate template = ScriptTemplate.Script)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(folder);
        if (ValidateClassName(className) is { } error)
            throw new ArgumentException(error, nameof(className));

        var root = options.SourceRoot;
        var directory = Path.GetFullPath(Path.IsPathRooted(folder) ? folder : Path.Combine(root, folder));
        var relative = Path.GetRelativePath(root, directory);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            throw new ArgumentException($"{directory} is not inside the scripts folder {root}.", nameof(folder));

        var path = Path.Combine(directory, className + ".cs");
        if (File.Exists(path))
            throw new IOException($"{Path.GetRelativePath(options.ProjectDirectory, path)} already exists.");
        var projectName = Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.ProjectDirectory)));
        var source = Render(template, className, NamespaceFor(projectName, relative == "." ? null : relative));
        Directory.CreateDirectory(directory);
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            writer.Write(source);
        return path;
    }

    /// <summary>Turns a name such as "isle-hopper" or "2d tools" into a C# identifier such as "IsleHopper" or "_2dTools".</summary>
    private static string Identifier(string text, string fallback)
    {
        var builder = new StringBuilder(text.Length);
        var upper = true;
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                builder.Append(upper ? char.ToUpperInvariant(c) : c);
                upper = false;
            }
            else
            {
                upper = true;
            }
        }

        if (builder.Length == 0)
            return fallback;
        if (char.IsDigit(builder[0]))
            builder.Insert(0, '_');
        var identifier = builder.ToString();
        return SyntaxFacts.GetKeywordKind(identifier) != SyntaxKind.None ? "@" + identifier : identifier;
    }
}
