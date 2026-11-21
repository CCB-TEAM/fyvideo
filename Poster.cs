namespace SkiaVideoLab;

/// <summary>
/// 静态输出：七拍预览图（sheet）与综合海报（poster）。
/// 画面直接从场景里渲染，保证和成片一模一样，不会出现"预览和成片不一致"。
/// </summary>
public static class Poster
{
    private static readonly (string Name, string En)[] BeatNames =
    [
        ("封面", "COVER"),
        ("数字", "NUMBERS"),
        ("六功能", "FEATURES"),
        ("技术", "UNDER THE HOOD"),
        ("后台", "ADMIN"),
        ("对比", "VERSUS"),
        ("落版", "COLOPHON"),
    ];

    /// <summary>把每一拍渲染成一张图，采样点取每拍的 62% 处（动效基本落位）。</summary>
    private static List<SKImage> RenderBeats(FyPosterDesign scene, out int width, out int height)
    {
        (width, height) = scene.Size;
        var beats = scene.Beats;
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var frames = new List<SKImage>();

        using var surface = SKSurface.Create(info);
        for (var i = 0; i < beats.Count; i++)
        {
            var (start, end) = beats[i];
            var t = start + (end - start) * 0.62;
            scene.Draw(surface.Canvas, t, (int)Math.Round(t * 30), width, height);
            frames.Add(surface.Snapshot());
        }

        return frames;
    }

    private static void Text(SKCanvas c, Typeset type, string s, float x, float y, float size, SKColor color,
        float tracking = 0, bool bold = false)
    {
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        type.Draw(c, s, x, y, size, paint, tracking, bold);
    }

    private static void Rule(SKCanvas c, float x1, float y1, float x2, float y2, float w, SKColor color)
    {
        using var paint = new SKPaint { Color = color, StrokeWidth = w, IsAntialias = true, Style = SKPaintStyle.Stroke };
        c.DrawLine(x1, y1, x2, y2, paint);
    }

    /// <summary>七拍预览：一排缩略图 + 编号 / 名称 / 时间码。</summary>
    public static void RenderSheet(FyPosterDesign scene, string outPath)
    {
        var frames = RenderBeats(scene, out var fw, out var fh);
        var portrait = fh > fw;

        const float margin = 72;
        const float gap = 18;
        var tileW = portrait ? 214f : 300f;
        var tileH = tileW * fh / fw;
        var labelBand = 96f;
        var totalW = margin * 2 + tileW * frames.Count + gap * (frames.Count - 1);
        var totalH = margin * 2 + tileH + labelBand;

        var info = new SKImageInfo((int)MathF.Ceiling(totalW), (int)MathF.Ceiling(totalH), SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;

        using var type = new Typeset();
        using var paper = new Paper();
        paper.DrawBackground(canvas, info.Width, info.Height);

        var beats = scene.Beats;
        for (var i = 0; i < frames.Count; i++)
        {
            var x = margin + i * (tileW + gap);
            var y = margin;
            var rect = new SKRect(x, y, x + tileW, y + tileH);

            using (var paint = new SKPaint { IsAntialias = true })
                canvas.DrawImage(frames[i], rect, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint);

            using (var border = new SKPaint { Color = Swiss.Ink, StrokeWidth = 2, IsAntialias = true, Style = SKPaintStyle.Stroke })
                canvas.DrawRect(rect, border);

            var (start, end) = beats[i];
            Text(canvas, type, $"{i + 1:00}", x, y + tileH + 34, 20, Swiss.Red, 1.5f, true);
            Text(canvas, type, BeatNames[i].Name, x + 34, y + tileH + 34, 20, Swiss.Ink, 0, true);
            Text(canvas, type, $"{start:0.0}–{end:0.0}s", x, y + tileH + 62, 15, Swiss.Ink.WithAlpha(170), 0.8f);
            Text(canvas, type, BeatNames[i].En, x, y + tileH + 82, 12, Swiss.Ink.WithAlpha(150), 1.8f, true);
        }

        using var data = surface.Snapshot().Encode(SKEncodedImageFormat.Png, 96);
        using var stream = File.Create(outPath);
        data.SaveTo(stream);
        Console.WriteLine($"完成：{outPath}（{info.Width}×{info.Height}）");
    }

    /// <summary>综合海报：标题 + 七拍拼版 + 落款，一张图讲完整个片子。</summary>
    public static void RenderPoster(FyPosterDesign scene, string outPath)
    {
        var frames = RenderBeats(scene, out var fw, out var fh);
        const int width = 2400;
        const int height = 1600;

        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;

        using var type = new Typeset();
        using var paper = new Paper();
        paper.DrawBackground(canvas, width, height);

        const float margin = 96;

        // 顶部：标题 + 副题
        Text(canvas, type, "FYSERVER", margin, 210, 150, Swiss.Ink, -6, true);
        Text(canvas, type, "KARDS 私服后端 · C# / .NET 10 · 38 秒七拍", margin, 262, 30, Swiss.Ink.WithAlpha(200));

        // 右上：红条 + 版本
        using (var paint = new SKPaint { Color = Swiss.Red, IsAntialias = false })
            canvas.DrawRect(new SKRect(width - margin - 340, 120, width - margin, 168), paint);
        Text(canvas, type, "PREVIEW · 7 BEATS", width - margin - 328, 152, 20, Swiss.Paper, 3, true);
        Text(canvas, type, "1920×1080 / 1080×1920 · 30fps", width - margin, 210, 18, Swiss.Ink.WithAlpha(170), 1.2f);
        Rule(canvas, width - margin - 340, 210, width - margin, 210, 1, Swiss.Ink.WithAlpha(120));

        Rule(canvas, margin, 300, width - margin, 300, 3, Swiss.Ink);

        // 七拍拼版：4 + 3
        const int cols = 4;
        const float gap = 20;
        var tileW = (width - margin * 2 - gap * (cols - 1)) / cols;
        var tileH = tileW * fh / fw;
        var rowGap = 54f;
        var top = 340f;

        for (var i = 0; i < frames.Count; i++)
        {
            var col = i % cols;
            var row = i / cols;
            var x = margin + col * (tileW + gap);
            var y = top + row * (tileH + rowGap);
            var rect = new SKRect(x, y, x + tileW, y + tileH);

            using (var paint = new SKPaint { IsAntialias = true })
                canvas.DrawImage(frames[i], rect, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint);

            using (var border = new SKPaint { Color = Swiss.Ink, StrokeWidth = 2, IsAntialias = true, Style = SKPaintStyle.Stroke })
                canvas.DrawRect(rect, border);

            var (start, end) = scene.Beats[i];
            Text(canvas, type, $"{i + 1:00}", x, y + tileH + 30, 18, Swiss.Red, 1.4f, true);
            Text(canvas, type, BeatNames[i].Name, x + 32, y + tileH + 30, 18, Swiss.Ink, 0, true);
            Text(canvas, type, $"{start:0.0}–{end:0.0}s", x + tileW, y + tileH + 30, 15, Swiss.Ink.WithAlpha(160), 0.8f);
        }

        // 底部：三个数字 + 红色块，把下半幅撑住
        var bandTop = 1080f;
        Rule(canvas, margin, bandTop, width - margin, bandTop, 3, Swiss.Ink);

        var stats = new[] { ("24", "MB 单文件 NativeAOT"), ("0", "预装运行时"), ("1", "端口跑 HTTP + WS") };
        for (var i = 0; i < stats.Length; i++)
        {
            var x = margin + i * 470f;
            Text(canvas, type, stats[i].Item1, x, bandTop + 170, 130, Swiss.Red, -6, true);
            Text(canvas, type, stats[i].Item2, x + 8, bandTop + 214, 24, Swiss.Ink, 0, true);
        }

        // 右下红块：片长与规格
        using (var paint = new SKPaint { Color = Swiss.Red, IsAntialias = false })
            canvas.DrawRect(new SKRect(width - margin - 520, bandTop + 40, width - margin, bandTop + 300), paint);
        Text(canvas, type, "38s", width - margin - 490, bandTop + 170, 120, Swiss.Paper, -6, true);
        Text(canvas, type, "7 BEATS · 30FPS · NO NARRATION", width - margin - 488, bandTop + 218, 18, Swiss.Paper.WithAlpha(235), 2.4f, true);

        // 落款
        var footY = height - 92;
        Rule(canvas, margin, footY - 52, width - margin, footY - 52, 1, Swiss.Ink.WithAlpha(130));
        // 平台审核不接受外链，落款只写项目名
        Text(canvas, type, "FYServer", margin, footY, 30, Swiss.Ink, -1, true);
        Text(canvas, type, "KARDS 私服后端 · MIT · 2026", margin + 180, footY, 22, Swiss.Ink.WithAlpha(190));
        Text(canvas, type, "纸黄 / 焦点红 · 瑞士国际主义 + 构成主义", margin + 520, footY, 20, Swiss.Ink.WithAlpha(170));

        using var data = surface.Snapshot().Encode(SKEncodedImageFormat.Png, 96);
        using var stream = File.Create(outPath);
        data.SaveTo(stream);
        Console.WriteLine($"完成：{outPath}（{width}×{height}）");
    }
}
