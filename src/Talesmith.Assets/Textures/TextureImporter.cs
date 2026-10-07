using Microsoft.Extensions.Logging;

namespace Talesmith.Assets.Textures;

/// <summary>Imports image files as <see cref="TextureAsset"/>s, applying their <see cref="TextureImportSettings"/>.</summary>
/// <remarks>Images larger than <see cref="TextureImportSettings.MaxSize"/> are scaled down, and their sprite slices with them.</remarks>
public sealed partial class TextureImporter : AssetImporter<TextureAsset, TextureImportSettings>
{
    public const string ImporterId = "texture";

    public override IReadOnlyList<string> Extensions { get; } = [".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".ico"];

    public override string Id => ImporterId;

    public override async Task<TextureAsset> ImportAsync(AssetImportContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var settings = context.GetSettings<TextureImportSettings>();
        var stream = context.OpenRead();
        Imaging.ImageData image;
        await using (stream.ConfigureAwait(false))
            image = TextureDecoder.Decode(stream, settings.PremultipliedAlpha);

        var scale = 1f;
        if (settings.MaxSize > 0 && Math.Max(image.Width, image.Height) > settings.MaxSize)
        {
            scale = settings.MaxSize / (float)Math.Max(image.Width, image.Height);
            image = TextureDecoder.Resize(image, Math.Max(1, (int)MathF.Round(image.Width * scale)), Math.Max(1, (int)MathF.Round(image.Height * scale)));
        }

        var sheet = SpriteSlicer.Slice(image, settings, AssetPath.GetFileNameWithoutExtension(context.Path), scale);
        foreach (var warning in sheet.Warnings)
            LogSliceWarning(context.Logger, context.Path, warning);

        return new TextureAsset(context.Path, image) { Sprites = sheet.Sprites, Animations = sheet.Animations, Settings = settings };
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Path}: {Warning}")]
    private static partial void LogSliceWarning(ILogger logger, string path, string warning);
}
