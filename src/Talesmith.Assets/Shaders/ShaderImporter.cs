using Microsoft.Extensions.Logging;
using Talesmith.Assets.Json;
using Talesmith.Rendering;

namespace Talesmith.Assets.Shaders;

/// <summary>What a shader is drawn with.</summary>
public enum ShaderStage
{
    /// <summary>A sprite shader used by a <see cref="Material"/>.</summary>
    Material,

    /// <summary>A full-screen shader used by a <see cref="PostEffect"/>.</summary>
    PostEffect
}

/// <summary>The contents of a <c>.tshader</c> file: a shader's source for each backend.</summary>
/// <remarks>
/// <code>
/// {
///   "version": 1,
///   "name": "flash",
///   "stage": "material",
///   "skSlFile": "flash.sksl",
///   "spirVFile": "flash.frag.spv"
/// }
/// </code>
/// SkSL can also be written inline as <c>"skSl"</c>. Files are relative to the .tshader file. Shaders are compiled to SPIR-V ahead of
/// time; backends compile nothing at import. See <see cref="ShaderSource"/> for what each backend expects.
/// </remarks>
public sealed record ShaderDefinition
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    /// <summary>The shader's name in logs and tools; null uses the file name.</summary>
    public string? Name { get; init; }

    public ShaderStage Stage { get; init; } = ShaderStage.Material;

    public string? SkSl { get; init; }

    public string? SkSlFile { get; init; }

    public string? SpirVFile { get; init; }
}

/// <summary>Imports <c>.tshader</c> files as <see cref="ShaderSource"/>s, checking that each source has the expected structure.</summary>
public sealed partial class ShaderImporter : AssetImporter<ShaderSource>
{
    public const string ImporterId = "shader";

    public override IReadOnlyList<string> Extensions { get; } = [".tshader"];

    public override string Id => ImporterId;

    public override async Task<ShaderSource> ImportAsync(AssetImportContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ShaderDefinition definition;
        var stream = context.OpenRead();
        await using (stream.ConfigureAwait(false))
            definition = await AssetJson.DeserializeAsync<ShaderDefinition>(stream, "The shader", cancellationToken).ConfigureAwait(false);
        if (definition.Version > ShaderDefinition.CurrentVersion)
            throw new AssetException($"The shader has version {definition.Version}, but this version of Talesmith reads up to {ShaderDefinition.CurrentVersion}.");
        if (definition.SkSl is not null && definition.SkSlFile is not null)
            throw new AssetException("The shader sets both skSl and skSlFile; use one.");

        var skSl = definition.SkSl;
        if (definition.SkSlFile is { } skSlFile)
            skSl = System.Text.Encoding.UTF8.GetString(await ReadAsync(context, skSlFile, cancellationToken).ConfigureAwait(false));
        var spirV = definition.SpirVFile is { } spirVFile ? await ReadAsync(context, spirVFile, cancellationToken).ConfigureAwait(false) : null;
        if (skSl is null && spirV is null)
            throw new AssetException("The shader has no source; set skSl, skSlFile or spirVFile.");

        var result = ShaderValidator.Validate(definition.Stage, skSl, spirV);
        if (result.Errors.Count > 0)
            throw new AssetException($"The shader is invalid: {string.Join(" ", result.Errors)}");
        foreach (var warning in result.Warnings)
            LogWarning(context.Logger, context.Path, warning);

        return new ShaderSource(definition.Name ?? AssetPath.GetFileNameWithoutExtension(context.Path), skSl, spirV);
    }

    private static async Task<byte[]> ReadAsync(AssetImportContext context, string relativePath, CancellationToken cancellationToken)
    {
        var path = context.Resolve(relativePath);
        if (!context.Source.Exists(path))
            throw new AssetException($"The shader file '{path}' does not exist.");

        using var buffer = new MemoryStream();
        var stream = context.Source.OpenRead(path);
        await using (stream.ConfigureAwait(false))
            await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Path}: {Warning}")]
    private static partial void LogWarning(ILogger logger, string path, string warning);
}

/// <summary>Problems found in a shader's sources.</summary>
public sealed record ShaderValidationResult(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);

/// <summary>Checks the structure the engine relies on in shader sources, without compiling them.</summary>
public static class ShaderValidator
{
    private const uint SpirVMagic = 0x07230203;

    public static ShaderValidationResult Validate(ShaderStage stage, string? skSl, ReadOnlySpan<byte> spirV)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        if (skSl is not null)
            ValidateSkSl(stage, skSl, errors, warnings);
        if (!spirV.IsEmpty)
            ValidateSpirV(spirV, errors);
        return new ShaderValidationResult(errors, warnings);
    }

    private static void ValidateSkSl(ShaderStage stage, string source, List<string> errors, List<string> warnings)
    {
        if (!source.Contains("main(", StringComparison.Ordinal) && !source.Contains("main (", StringComparison.Ordinal))
            errors.Add("The SkSL source has no main function.");

        var input = stage == ShaderStage.Material ? "image" : "scene";
        if (!ContainsDeclaration(source, $"uniform shader {input}"))
            errors.Add($"The SkSL {(stage == ShaderStage.Material ? "material" : "post effect")} shader must declare 'uniform shader {input};'.");
        if (source.Contains("params", StringComparison.Ordinal) && !ContainsDeclaration(source, "uniform float4 params[4]") && !ContainsDeclaration(source, "uniform half4 params[4]"))
            warnings.Add("The SkSL source uses params but does not declare 'uniform float4 params[4];', so they will not be set.");

        var depth = 0;
        foreach (var character in source)
        {
            depth += character switch { '{' => 1, '}' => -1, _ => 0 };
            if (depth < 0)
                break;
        }

        if (depth != 0)
            errors.Add("The SkSL source has unbalanced braces.");
    }

    private static bool ContainsDeclaration(string source, string declaration) =>
        string.Join(' ', source.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Contains(declaration, StringComparison.Ordinal);

    private static void ValidateSpirV(ReadOnlySpan<byte> spirV, List<string> errors)
    {
        if (spirV.Length < 20 || spirV.Length % 4 != 0)
        {
            errors.Add("The SPIR-V file is not a whole number of 32-bit words with a header.");
            return;
        }

        var magic = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(spirV);
        if (magic == System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(SpirVMagic))
            errors.Add("The SPIR-V file is big-endian; compile it for little-endian targets.");
        else if (magic != SpirVMagic)
            errors.Add("The SPIR-V file does not start with the SPIR-V magic number.");
    }
}
