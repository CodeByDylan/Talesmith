using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Talesmith.Assets;
using Talesmith.Assets.Database;

namespace Talesmith.Build.Content;

/// <summary>Finds assets that code loads by path, such as <c>Audio.Play("audio/jump.wav")</c>, from string literals in scripts and plugins.</summary>
/// <remarks>
/// A literal that is an asset's path includes the asset; a literal that is a folder path ending in a slash, such as the start of
/// <c>$"cutscenes/{name}.cutscene"</c>, includes the whole folder. Paths built any other way need the build settings' always-include list.
/// </remarks>
public static class CodeReferences
{
    /// <summary>The string literals in C# source files, including the text parts of interpolated strings.</summary>
    public static IEnumerable<string> FromSources(IEnumerable<string> files, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);
        foreach (var file in files)
        {
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file), cancellationToken: cancellationToken);
            foreach (var token in tree.GetRoot(cancellationToken).DescendantTokens())
            {
                if (token.IsKind(SyntaxKind.StringLiteralToken) || token.IsKind(SyntaxKind.InterpolatedStringTextToken)
                                                               || token.IsKind(SyntaxKind.SingleLineRawStringLiteralToken))
                    yield return token.ValueText;
            }
        }
    }

    /// <summary>The string literals compiled into an assembly, read from its metadata without loading it.</summary>
    /// <exception cref="BadImageFormatException">The file is not a .NET assembly.</exception>
    public static IReadOnlyList<string> FromAssembly(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        if (!pe.HasMetadata)
            return [];
        var reader = pe.GetMetadataReader();
        var strings = new List<string>();
        var size = reader.GetHeapSize(HeapIndex.UserString);
        var handle = MetadataTokens.UserStringHandle(1);
        while (!handle.IsNil && MetadataTokens.GetHeapOffset(handle) < size)
        {
            strings.Add(reader.GetUserString(handle));
            handle = reader.GetNextHandle(handle);
        }

        return strings;
    }

    /// <summary>The assets and folders the literals name.</summary>
    public static IEnumerable<ContentRoot> Resolve(IEnumerable<string> literals, AssetDatabase assets, string source)
    {
        ArgumentNullException.ThrowIfNull(literals);
        ArgumentNullException.ThrowIfNull(assets);
        var seen = new HashSet<string>(AssetPath.Comparer);
        foreach (var literal in literals)
        {
            if (literal.Length is < 3 or > 260 || !literal.Contains('.') && !literal.Contains('/') || literal.AsSpan().ContainsAny('\n', '"', '*'))
                continue;
            var folder = literal.EndsWith('/');
            string path;
            try
            {
                path = AssetPath.Normalize(literal.Trim());
            }
            catch (AssetException)
            {
                continue;
            }

            if (path.Length == 0 || !seen.Add(path) || !assets.TryGetAsset(path, out var record) || record.IsFolder != folder)
                continue;
            yield return ContentRoot.ForPath(path, ContentReason.CodeReference, source);
        }
    }
}
