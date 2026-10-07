using SkiaSharp;

namespace Talesmith.Editor.Projects.Templates;

/// <summary>Draws the starter artwork of the project templates, so new projects show something without bundled files.</summary>
internal static class TemplateArt
{
    public const int HeroFrame = 32;
    public const int HeroFrames = 4;
    public const int Tile = 32;
    public const int HexWidth = 56;
    public const int HexHeight = 64;

    /// <summary>A small adventurer in four idle frames, side by side.</summary>
    public static SKBitmap Hero(SKColor tunic)
    {
        var bitmap = new SKBitmap(HeroFrame * HeroFrames, HeroFrame, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        for (var frame = 0; frame < HeroFrames; frame++)
        {
            var bob = frame is 1 or 2 ? 1 : 0;
            canvas.Save();
            canvas.Translate(frame * HeroFrame, bob);
            using var outline = Fill(new SKColor(0x22, 0x1B, 0x2E));
            using var skin = Fill(new SKColor(0xF6, 0xC9, 0x9F));
            using var body = Fill(tunic);
            using var hair = Fill(new SKColor(0x6B, 0x3E, 0x26));
            using var boots = Fill(new SKColor(0x4A, 0x32, 0x28));
            using var white = Fill(SKColors.White);
            canvas.DrawOval(new SKRect(8, 28, 24, 31), Fill(new SKColor(0, 0, 0, 60)));
            canvas.DrawRoundRect(new SKRect(9, 25, 15, 30), 1.5f, 1.5f, boots);
            canvas.DrawRoundRect(new SKRect(17, 25, 23, 30), 1.5f, 1.5f, boots);
            canvas.DrawRoundRect(new SKRect(7.5f, 15, 24.5f, 27), 4, 4, outline);
            canvas.DrawRoundRect(new SKRect(8.5f, 16, 23.5f, 26), 3.5f, 3.5f, body);
            canvas.DrawRect(new SKRect(8.5f, 21, 23.5f, 22.5f), Fill(new SKColor(0x3B, 0x2A, 0x20)));
            canvas.DrawCircle(16, 11, 8.5f, outline);
            canvas.DrawCircle(16, 11, 7.5f, skin);
            canvas.DrawArc(new SKRect(8.5f, 3.5f, 23.5f, 18), 180, 180, true, hair);
            var blink = frame == 3;
            if (blink)
            {
                canvas.DrawRect(new SKRect(11.5f, 12, 14, 13), outline);
                canvas.DrawRect(new SKRect(18, 12, 20.5f, 13), outline);
            }
            else
            {
                canvas.DrawRect(new SKRect(12, 10.5f, 14, 13.5f), outline);
                canvas.DrawRect(new SKRect(18, 10.5f, 20, 13.5f), outline);
                canvas.DrawRect(new SKRect(12, 10.5f, 13, 11.5f), white);
                canvas.DrawRect(new SKRect(18, 10.5f, 19, 11.5f), white);
            }

            canvas.DrawCircle(11, 15, 1.4f, Fill(new SKColor(0xF0, 0x8A, 0x8A, 160)));
            canvas.DrawCircle(21, 15, 1.4f, Fill(new SKColor(0xF0, 0x8A, 0x8A, 160)));
            canvas.Restore();
        }

        return bitmap;
    }

    /// <summary>Platformer tiles: grass top, dirt, stone, a floating platform, and a coin in four frames.</summary>
    public static SKBitmap PlatformerTiles()
    {
        var bitmap = new SKBitmap(Tile * 4, Tile * 2, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        var dirt = new SKColor(0x9A, 0x63, 0x3F);
        var dirtDark = new SKColor(0x7A, 0x4A, 0x2E);
        var grass = new SKColor(0x5F, 0xB0, 0x4A);
        var grassLight = new SKColor(0x8B, 0xD3, 0x5E);

        DirtTile(canvas, 0, 0, dirt, dirtDark);
        canvas.DrawRect(new SKRect(0, 0, Tile, 10), Fill(grass));
        canvas.DrawRect(new SKRect(0, 0, Tile, 3), Fill(grassLight));
        for (var x = 2; x < Tile; x += 6)
            canvas.DrawRect(new SKRect(x, 10, x + 3, 13), Fill(grass));

        DirtTile(canvas, Tile, 0, dirt, dirtDark);

        var stone = new SKColor(0x8C, 0x93, 0xA3);
        canvas.DrawRect(new SKRect(Tile * 2, 0, Tile * 3, Tile), Fill(new SKColor(0x6D, 0x73, 0x82)));
        canvas.DrawRoundRect(new SKRect(Tile * 2 + 1, 1, Tile * 2 + 15, 15), 2, 2, Fill(stone));
        canvas.DrawRoundRect(new SKRect(Tile * 2 + 17, 1, Tile * 3 - 1, 15), 2, 2, Fill(stone));
        canvas.DrawRoundRect(new SKRect(Tile * 2 + 1, 17, Tile * 2 + 23, Tile - 1), 2, 2, Fill(stone));
        canvas.DrawRoundRect(new SKRect(Tile * 2 + 25, 17, Tile * 3 - 1, Tile - 1), 2, 2, Fill(stone));

        var wood = new SKColor(0xC2, 0x8A, 0x4E);
        canvas.DrawRoundRect(new SKRect(Tile * 3, 2, Tile * 4, 14), 3, 3, Fill(new SKColor(0x8A, 0x5A, 0x30)));
        canvas.DrawRoundRect(new SKRect(Tile * 3 + 1, 3, Tile * 4 - 1, 12), 2, 2, Fill(wood));
        canvas.DrawRect(new SKRect(Tile * 3 + 15, 3, Tile * 3 + 17, 12), Fill(new SKColor(0x8A, 0x5A, 0x30)));

        for (var frame = 0; frame < 4; frame++)
        {
            var width = frame switch { 0 => 11f, 1 => 7f, 2 => 2.5f, _ => 7f };
            var cx = frame * Tile + Tile / 2f;
            var cy = Tile + Tile / 2f;
            canvas.DrawOval(new SKRect(cx - width - 1, cy - 12, cx + width + 1, cy + 12), Fill(new SKColor(0xB8, 0x86, 0x0B)));
            canvas.DrawOval(new SKRect(cx - width, cy - 11, cx + width, cy + 11), Fill(new SKColor(0xFF, 0xD5, 0x4A)));
            if (width > 4)
                canvas.DrawOval(new SKRect(cx - width * 0.45f, cy - 7, cx + width * 0.15f, cy + 3), Fill(new SKColor(0xFF, 0xF1, 0xB0)));
        }

        return bitmap;
    }

    /// <summary>Pointy-top hex tiles: deep water, shallow water, sand, grass, forest and mountain.</summary>
    public static SKBitmap HexTiles()
    {
        (SKColor Base, SKColor Detail)[] terrains =
        [
            (new SKColor(0x2B, 0x6C, 0xB0), new SKColor(0x3C, 0x82, 0xC8)),
            (new SKColor(0x4A, 0x9F, 0xD0), new SKColor(0x7C, 0xC4, 0xE8)),
            (new SKColor(0xE8, 0xD3, 0x8F), new SKColor(0xD4, 0xBA, 0x6E)),
            (new SKColor(0x6D, 0xB8, 0x4F), new SKColor(0x8F, 0xD0, 0x62)),
            (new SKColor(0x4C, 0x94, 0x45), new SKColor(0x2E, 0x6B, 0x35)),
            (new SKColor(0x8E, 0x8A, 0x86), new SKColor(0xF2, 0xF2, 0xF4))
        ];
        var bitmap = new SKBitmap(HexWidth * terrains.Length, HexHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        for (var i = 0; i < terrains.Length; i++)
        {
            var (fill, detail) = terrains[i];
            var x = i * HexWidth;
            using var hex = HexPath(x, 0);
            canvas.DrawPath(hex, Fill(fill));
            canvas.Save();
            canvas.ClipPath(hex, antialias: true);
            switch (i)
            {
                case 0 or 1:
                    using (var wave = Stroke(detail, 2))
                    {
                        canvas.DrawArc(new SKRect(x + 12, 22, x + 26, 32), 200, 140, false, wave);
                        canvas.DrawArc(new SKRect(x + 28, 36, x + 42, 46), 200, 140, false, wave);
                    }

                    break;
                case 2:
                    canvas.DrawCircle(x + 20, 26, 1.5f, Fill(detail));
                    canvas.DrawCircle(x + 34, 38, 1.5f, Fill(detail));
                    canvas.DrawCircle(x + 26, 44, 1.2f, Fill(detail));
                    break;
                case 3:
                    using (var blade = Stroke(detail, 1.5f))
                    {
                        canvas.DrawLine(x + 18, 30, x + 20, 25, blade);
                        canvas.DrawLine(x + 34, 42, x + 36, 37, blade);
                        canvas.DrawLine(x + 28, 22, x + 29, 18, blade);
                    }

                    break;
                case 4:
                    Tree(canvas, x + 20, 38, detail);
                    Tree(canvas, x + 34, 30, detail);
                    Tree(canvas, x + 30, 46, detail);
                    break;
                case 5:
                    using (var mountain = new SKPath())
                    {
                        mountain.MoveTo(x + 10, 46);
                        mountain.LineTo(x + 28, 16);
                        mountain.LineTo(x + 46, 46);
                        mountain.Close();
                        canvas.DrawPath(mountain, Fill(new SKColor(0x6E, 0x69, 0x66)));
                    }

                    using (var snow = new SKPath())
                    {
                        snow.MoveTo(x + 22, 26);
                        snow.LineTo(x + 28, 16);
                        snow.LineTo(x + 34, 26);
                        snow.LineTo(x + 30, 24);
                        snow.LineTo(x + 28, 27);
                        snow.LineTo(x + 25, 24);
                        snow.Close();
                        canvas.DrawPath(snow, Fill(detail));
                    }

                    break;
            }

            canvas.Restore();
            canvas.DrawPath(hex, Stroke(new SKColor(0, 0, 0, 40), 1));
        }

        return bitmap;
    }

    /// <summary>The empty template's mark: a rounded tile with the Talesmith gradient and a star.</summary>
    public static SKBitmap Logo()
    {
        const int size = 96;
        var bitmap = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        using var gradient = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(size, size),
                [new SKColor(0x8B, 0x8E, 0xFA), new SKColor(0x5B, 0x4F, 0xE0)], SKShaderTileMode.Clamp)
        };
        canvas.DrawRoundRect(new SKRect(4, 4, size - 4, size - 4), 22, 22, gradient);
        using var star = new SKPath();
        for (var i = 0; i < 10; i++)
        {
            var radius = i % 2 == 0 ? 30 : 13;
            var angle = Math.PI / 5 * i - Math.PI / 2;
            var point = new SKPoint(size / 2f + (float)(Math.Cos(angle) * radius), size / 2f + 2 + (float)(Math.Sin(angle) * radius));
            if (i == 0)
                star.MoveTo(point);
            else
                star.LineTo(point);
        }

        star.Close();
        canvas.DrawPath(star, Fill(SKColors.White));
        return bitmap;
    }

    public static byte[] EncodePng(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static SKPath HexPath(float x, float y)
    {
        var path = new SKPath();
        var cx = x + HexWidth / 2f;
        var cy = y + HexHeight / 2f;
        for (var i = 0; i < 6; i++)
        {
            var angle = Math.PI / 3 * i - Math.PI / 2;
            var point = new SKPoint(cx + (float)(Math.Cos(angle) * HexHeight / 2), cy + (float)(Math.Sin(angle) * HexHeight / 2));
            if (i == 0)
                path.MoveTo(point);
            else
                path.LineTo(point);
        }

        path.Close();
        return path;
    }

    private static void DirtTile(SKCanvas canvas, float x, float y, SKColor dirt, SKColor dark)
    {
        canvas.DrawRect(new SKRect(x, y, x + Tile, y + Tile), Fill(dirt));
        canvas.DrawRect(new SKRect(x + 5, y + 16, x + 9, y + 19), Fill(dark));
        canvas.DrawRect(new SKRect(x + 20, y + 22, x + 24, y + 25), Fill(dark));
        canvas.DrawRect(new SKRect(x + 13, y + 28, x + 16, y + 30), Fill(dark));
    }

    private static void Tree(SKCanvas canvas, float x, float y, SKColor leaves)
    {
        canvas.DrawRect(new SKRect(x - 1.5f, y, x + 1.5f, y + 5), Fill(new SKColor(0x6B, 0x45, 0x2A)));
        canvas.DrawCircle(x, y - 3, 6.5f, Fill(leaves));
        canvas.DrawCircle(x - 2, y - 5, 2.5f, Fill(new SKColor(0x4E, 0x8F, 0x4A)));
    }

    private static SKPaint Fill(SKColor color) => new() { Color = color, IsAntialias = true, Style = SKPaintStyle.Fill };

    private static SKPaint Stroke(SKColor color, float width) =>
        new() { Color = color, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = width, StrokeCap = SKStrokeCap.Round };
}
