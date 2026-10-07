namespace Talesmith.Assets.Fonts;

/// <summary>Imports TrueType and OpenType fonts as <see cref="FontAsset"/>s.</summary>
public sealed class FontImporter : AssetImporter<FontAsset, FontImportSettings>
{
    public const string ImporterId = "font";

    public override IReadOnlyList<string> Extensions { get; } = [".ttf", ".otf", ".ttc"];

    public override string Id => ImporterId;

    public override async Task<FontAsset> ImportAsync(AssetImportContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var settings = context.GetSettings<FontImportSettings>();
        using var buffer = new MemoryStream();
        var stream = context.OpenRead();
        await using (stream.ConfigureAwait(false))
            await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

        var data = buffer.ToArray();
        var (family, style) = OpenTypeNames.Read(data);
        return new FontAsset(context.Path, data, string.IsNullOrWhiteSpace(settings.FamilyName) ? family : settings.FamilyName, style, settings);
    }
}
