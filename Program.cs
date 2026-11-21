using System.Diagnostics;

namespace SkiaVideoLab;

/// <summary>
/// 极简 CLI：
///   video [--scene demo|vertical] [--out out/demo.mp4] [--fps 30] [--seconds 6]
///   still --at 1.5 [--out out/frame.png] [--scene demo]
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return 0;
        }

        var command = args[0];
        var opts = ParseOptions(args.Skip(1).ToArray());

        var scene = BuildScene(opts);
        var (width, height) = scene.Size;
        var fps = opts.Int("fps", 30);
        var seconds = opts.Double("seconds", scene is FyPosterDesign design ? design.Duration : 6);
        var frames = opts.Int("frames", (int)Math.Round(seconds * fps));

        var outPath = Path.GetFullPath(opts.Get("out", "out/demo.mp4"));
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

        return command switch
        {
            "video" => RenderVideo(scene, width, height, fps, frames, outPath, opts),
            "still" => RenderStill(scene, width, height, outPath, opts),
            "fonts" => ProbeFonts(opts),
            "validate" => Validate(scene, width, height),
            "sheet" => RunPoster(scene, Path.ChangeExtension(outPath, ".png"), sheet: true),
            "poster" => RunPoster(scene, Path.ChangeExtension(outPath, ".png"), sheet: false),
            _ => Fail($"未知命令：{command}"),
        };
    }

    private static int RunPoster(IScene scene, string output, bool sheet)
    {
        if (scene is not FyPosterDesign poster)
        {
            Console.Error.WriteLine("sheet / poster 只支持 fy / fyv");
            return 2;
        }

        if (sheet) Poster.RenderSheet(poster, output);
        else Poster.RenderPoster(poster, output);
        return 0;
    }

    /// <summary>
    /// 排版验证：逐拍渲染（不落盘），用 OverlapValidator 检查
    /// 文字重叠 / 骑在色块边缘 / 低对比 / 越界。
    /// </summary>
    private static int Validate(IScene scene, int width, int height)
    {
        if (scene is not FyPosterDesign poster)
        {
            Console.Error.WriteLine("validate 只支持 fy / fyv");
            return 2;
        }

        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);

        var total = 0;
        foreach (var (start, end) in poster.Beats)
        {
            foreach (var frac in new[] { 0.6, 0.85 })
            {
                var t = start + (end - start) * frac;
                poster.Draw(surface.Canvas, t, (int)Math.Round(t * 30), width, height);

                var violations = OverlapValidator.Analyze(poster.Log.Items, poster.CurrentSafeArea);
                if (violations.Count == 0) continue;

                Console.WriteLine($"拍 {start:F1}–{end:F1}s  t={t:F2}s  {violations.Count} 处问题：");
                foreach (var v in violations)
                    Console.WriteLine($"   [{v.Rule}] {v.Detail}");
                total += violations.Count;
            }
        }

        Console.WriteLine(total == 0
            ? $"通过：{width}x{height} 全部节拍无越界 / 无意外重叠 / 对比度达标"
            : $"共 {total} 处问题");

        return total == 0 ? 0 : 1;
    }

    private static IScene BuildScene(Options opts)
        => opts.Get("scene", "fy") switch
        {
            "fy" => new FyPosterDesign(),
            "fyv" => new FyPosterDesign { Vertical = true },
            var other => throw new ArgumentException($"未知场景：{other}（可选 fy / fyv）"),
        };

    private static int RenderVideo(IScene scene, int width, int height, int fps, int frames, string output, Options opts)
    {
        var ffmpegPath = Environment.GetEnvironmentVariable("FFMPEG") ?? "ffmpeg";

        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        using var pipe = FfmpegPipe.Start(new FfmpegPipeSettings(
            FfmpegPath: ffmpegPath,
            Width: width,
            Height: height,
            Fps: fps,
            Output: output,
            Crf: opts.Int("crf", 18),
            Preset: opts.Get("preset", "medium"),
            AudioPath: opts.Has("audio") ? opts.Get("audio", null) : null));

        var buffer = new byte[width * height * 4];
        var sw = Stopwatch.StartNew();

        for (var frame = 0; frame < frames; frame++)
        {
            var t = frame / (double)fps;
            scene.Draw(surface.Canvas, t, frame, width, height);
            surface.Canvas.Flush();

            using (var image = surface.Snapshot())
            {
                unsafe
                {
                    fixed (byte* p = buffer)
                        image.ReadPixels(info, (IntPtr)p, width * 4, 0, 0);
                }
            }

            pipe.WriteFrame(buffer);

            if (frame % 30 == 0 || frame == frames - 1)
                Console.WriteLine($"  帧 {frame + 1}/{frames}  {sw.Elapsed.TotalSeconds:F1}s");
        }

        pipe.Finish();
        sw.Stop();

        Console.WriteLine($"完成：{output}");
        Console.WriteLine($"  帧数 {frames} · {width}x{height}@{fps} · 用时 {sw.Elapsed.TotalSeconds:F1}s");
        return 0;
    }

    private static int RenderStill(IScene scene, int width, int height, string output, Options opts)
    {
        var at = opts.Double("at", 1.0);
        var fps = opts.Int("fps", 30);

        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        scene.Draw(surface.Canvas, at, (int)Math.Round(at * fps), width, height);
        surface.Canvas.Flush();

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(output);
        data.SaveTo(stream);

        Console.WriteLine($"完成：{output}（t={at:F2}s）");
        return 0;
    }

    /// <summary>字体自检：确认中文字形不是 .notdef（豆腐块）。</summary>
    private static int ProbeFonts(Options opts)
    {
        var sample = opts.Get("text", "Skia 帧管线已就位 · ABC 123");
        using var bold = Fonts.Resolve(SKFontStyle.Bold);
        using var regular = Fonts.Resolve(SKFontStyle.Normal);

        using var ts = new Typeset();
        Console.WriteLine($"typeset = {ts.Describe()}");
        Console.WriteLine($"bold    = {bold.FamilyName} ({bold.FontWeight}, {bold.FontWidth}, {bold.FontSlant})");
        Console.WriteLine($"regular = {regular.FamilyName} ({regular.FontWeight})");

        using var font = new SKFont(regular, 36);
        var glyphs = font.GetGlyphs(sample);
        Console.WriteLine($"glyph count = {glyphs.Length} / chars = {sample.Length}");

        var missing = new List<char>();
        for (var i = 0; i < sample.Length && i < glyphs.Length; i++)
        {
            if (glyphs[i] == 0) missing.Add(sample[i]);
        }

        Console.WriteLine(missing.Count == 0
            ? "全部字形命中（没有 .notdef）"
            : $"缺失字形：{string.Join(" ", missing.Select(c => $"'{c}' U+{(int)c:X4}"))}");

        return missing.Count == 0 ? 0 : 1;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 2;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            用法：
              video    [--scene fy|fyv] [--out out/demo.mp4] [--fps 30] [--crf 18] [--preset medium]
              still    --at 12.5 [--out out/frame.png] [--scene fy]
              validate [--scene fy|fyv]        排版验证（重叠 / 骑线 / 对比度 / 越界）
              sheet    [--scene fy|fyv]        七拍预览图（PNG）
              poster   [--scene fy|fyv]        综合海报（PNG，2400×1600）
              fonts                            字体自检

            环境变量：
              FFMPEG   ffmpeg 可执行文件路径（默认取 PATH 上的 ffmpeg）
            """);
    }

    private sealed class Options(Dictionary<string, string?> values)
    {
        public bool Has(string key) => values.ContainsKey(key);

        public string Get(string key, string? fallback)
            => values.TryGetValue(key, out var v) && v is not null ? v : (fallback ?? string.Empty);

        public int Int(string key, int fallback)
            => values.TryGetValue(key, out var v) && int.TryParse(v, out var parsed) ? parsed : fallback;

        public double Double(string key, double fallback)
            => values.TryGetValue(key, out var v) && double.TryParse(v, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
    }

    private static Options ParseOptions(string[] args)
    {
        var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal)) continue;

            var key = arg[2..];
            var eq = key.IndexOf('=');
            if (eq >= 0)
            {
                dict[key[..eq]] = key[(eq + 1)..];
                continue;
            }

            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                dict[key] = args[++i];
            else
                dict[key] = "true";
        }

        return new Options(dict);
    }
}
