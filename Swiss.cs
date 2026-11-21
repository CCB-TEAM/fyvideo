namespace SkiaVideoLab;

/// <summary>瑞士国际主义 + 构成主义的调色板：纸黄底、墨黑字、焦点红。</summary>
public static class Swiss
{
    public static readonly SKColor Paper = new(0xF2, 0xE7, 0xCE);
    public static readonly SKColor Ink = new(0x12, 0x10, 0x0E);
    public static readonly SKColor Red = new(0xD1, 0x28, 0x1C);
    public static readonly SKColor Muted = new(0x6B, 0x64, 0x59);
    public static readonly SKColor White = new(0xFF, 0xFF, 0xFF);
    public static readonly SKColor Hair = new(0x9A, 0x8F, 0x7C);
}

/// <summary>
/// 混排排版：拉丁走 Arial（Helvetica 度量），中日韩走 Microsoft YaHei UI。
/// 按字符分段绘制，所以同一行里中英文都拿到正确的字形与字宽。
/// </summary>
public sealed class Typeset : IDisposable
{
    private readonly SKTypeface _latin;
    private readonly SKTypeface _latinBold;
    private readonly SKTypeface _cjk;
    private readonly SKTypeface _cjkBold;
    private readonly Dictionary<(float Size, bool Cjk, bool Bold), SKFont> _fonts = new();

    public Typeset()
    {
        _latin = SKTypeface.FromFamilyName("Arial", SKFontStyle.Normal) ?? SKTypeface.Default;
        _latinBold = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold) ?? _latin;
        _cjk = SKTypeface.FromFamilyName("Microsoft YaHei UI", SKFontStyle.Normal)
               ?? SKTypeface.FromFamilyName("Microsoft YaHei", SKFontStyle.Normal)
               ?? _latin;
        _cjkBold = SKTypeface.FromFamilyName("Microsoft YaHei UI", SKFontStyle.Bold)
                   ?? SKTypeface.FromFamilyName("Microsoft YaHei", SKFontStyle.Bold)
                   ?? _latinBold;
    }

    public SKTypeface Latin => _latin;

    public string Describe() =>
        $"latin={_latin.FamilyName}({(int)_latin.FontWeight})/{_latinBold.FamilyName}({(int)_latinBold.FontWeight}) " +
        $"cjk={_cjk.FamilyName}({(int)_cjk.FontWeight})/{_cjkBold.FamilyName}({(int)_cjkBold.FontWeight})";

    private static bool IsCjk(char c)
        => c is >= '\u2E80' and <= '\u9FFF'      // CJK 部首扩展 → 统一表意文字
           or >= '\uF900' and <= '\uFAFF'        // 兼容表意
           or >= '\uFF00' and <= '\uFF60'        // 全角标点/字母
           or >= '\u3000' and <= '\u303F';       // CJK 标点

    private SKFont Font(float size, bool cjk, bool bold)
    {
        var key = (size, cjk, bold);
        if (_fonts.TryGetValue(key, out var cached)) return cached;
        var typeface = (cjk, bold) switch
        {
            (true, true) => _cjkBold,
            (true, false) => _cjk,
            (false, true) => _latinBold,
            _ => _latin,
        };
        var font = new SKFont(typeface, size) { Subpixel = true, Edging = SKFontEdging.SubpixelAntialias };
        _fonts[key] = font;
        return font;
    }

    public float Measure(string text, float size, float tracking = 0, bool bold = false)
    {
        var width = 0f;
        var i = 0;
        while (i < text.Length)
        {
            var cjk = IsCjk(text[i]);
            var j = i;
            while (j < text.Length && IsCjk(text[j]) == cjk) j++;
            var run = text[i..j];
            var font = Font(size, cjk, bold);
            width += tracking == 0 ? font.MeasureText(run) : run.Sum(ch => font.MeasureText(ch.ToString()) + tracking);
            i = j;
        }

        return width;
    }

    public void Draw(SKCanvas canvas, string text, float x, float baseline, float size, SKPaint paint, float tracking = 0,
        bool bold = false)
    {
        var cursor = x;
        var i = 0;
        while (i < text.Length)
        {
            var cjk = IsCjk(text[i]);
            var j = i;
            while (j < text.Length && IsCjk(text[j]) == cjk) j++;
            var run = text[i..j];
            var font = Font(size, cjk, bold);

            if (tracking == 0)
            {
                canvas.DrawText(run, cursor, baseline, SKTextAlign.Left, font, paint);
                cursor += font.MeasureText(run);
            }
            else
            {
                foreach (var ch in run)
                {
                    canvas.DrawText(ch.ToString(), cursor, baseline, SKTextAlign.Left, font, paint);
                    cursor += font.MeasureText(ch.ToString()) + tracking;
                }
            }

            i = j;
        }
    }

    public void DrawRight(SKCanvas canvas, string text, float right, float baseline, float size, SKPaint paint,
        float tracking = 0, bool bold = false)
        => Draw(canvas, text, right - Measure(text, size, tracking, bold), baseline, size, paint, tracking, bold);

    /// <summary>文本包围盒（含 ascent/descent），供排版验证器使用。</summary>
    public SKRect Bounds(string text, float x, float baseline, float size, float tracking = 0, bool bold = false)
    {
        var width = Measure(text, size, tracking, bold);
        var font = Font(size, false, bold);
        var metrics = font.Metrics;
        // 用墨迹范围而不是行高：行高里 ascent 上方、descent 下方大片是空白，
        // 直接拿去判重叠会把"看着不挨着"的两块字判成重叠。
        return new SKRect(x, baseline + metrics.Ascent * 0.88f, x + width, baseline + metrics.Descent * 0.42f);
    }

    public void Dispose()
    {
        foreach (var font in _fonts.Values) font.Dispose();
        _fonts.Clear();
        _latin.Dispose();
        _cjk.Dispose();
    }
}

/// <summary>
/// 纸：底色 + 低频斑驳 + 高频颗粒 + 边缘压暗。全部用乘法混合，颗粒永远在字下面，
/// 所以文字保持锐利、背景有纸感。
/// </summary>
public sealed class Paper : IDisposable
{
    private readonly SKImage _grain;
    private readonly SKImage _mottle;
    private readonly SKImage _fiber;
    private readonly SKImage _ink;

    public Paper(int seed = 20261031)
    {
        _grain = BuildGrain(seed);
        _mottle = BuildMottle(seed + 7);
        _fiber = BuildFiber(seed + 13);
        _ink = BuildInk(seed + 29);
    }

    private static SKImage BuildGrain(int seed)
    {
        const int size = 512;
        using var bitmap = new SKBitmap(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        var rnd = new Random(seed);
        using var dot = new SKPaint { IsAntialias = false, StrokeWidth = 1 };

        // 高频颗粒：大量 1px 灰点
        for (var i = 0; i < 90000; i++)
        {
            var v = (byte)rnd.Next(182, 252);
            dot.Color = new SKColor(v, v, v, 255);
            canvas.DrawPoint(rnd.Next(size), rnd.Next(size), dot);
        }

        // 纸纤维：稀疏的短横线，方向随机
        using var fiber = new SKPaint { IsAntialias = true, StrokeWidth = 1, Style = SKPaintStyle.Stroke };
        for (var i = 0; i < 220; i++)
        {
            var v = (byte)rnd.Next(178, 240);
            fiber.Color = new SKColor(v, v, v, 150);
            var x = rnd.Next(size);
            var y = rnd.Next(size);
            var len = rnd.Next(8, 46);
            var dy = rnd.Next(-2, 3);
            canvas.DrawLine(x, y, Math.Min(size, x + len), y + dy, fiber);
        }

        return SKImage.FromBitmap(bitmap);
    }

    private static SKImage BuildMottle(int seed)
    {
        const int size = 32;
        using var bitmap = new SKBitmap(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);
        var rnd = new Random(seed);
        var values = new byte[size, size];
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
                values[x, y] = (byte)rnd.Next(236, 256);

        // 邻域平均，得到大块的低频起伏
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var sum = 0;
                var n = 0;
                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var nx = (x + dx + size) % size;
                        var ny = (y + dy + size) % size;
                        sum += values[nx, ny];
                        n++;
                    }
                }

                var v = (byte)(sum / n);
                bitmap.SetPixel(x, y, new SKColor(v, v, v, 255));
            }
        }

        return SKImage.FromBitmap(bitmap);
    }

    public void DrawBackground(SKCanvas canvas, int width, int height)
    {
        using (var paint = new SKPaint { Color = Swiss.Paper, IsAntialias = false })
            canvas.DrawRect(new SKRect(0, 0, width, height), paint);

        // 低频斑驳：放大到整幅，双线性插值——纸浆分布不均的那层
        using (var paint = new SKPaint
               {
                   Color = SKColors.White.WithAlpha(200),
                   BlendMode = SKBlendMode.Multiply,
               })
        {
            canvas.DrawImage(_mottle, new SKRect(0, 0, width, height),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None), paint);
        }

        // 高频颗粒：原尺寸平铺——纸面粗糙的那层
        using (var shader = SKShader.CreateImage(_grain, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat))
        using (var paint = new SKPaint
               {
                   Shader = shader,
                   Color = SKColors.White.WithAlpha(165),
                   BlendMode = SKBlendMode.Multiply,
               })
        {
            canvas.DrawRect(new SKRect(0, 0, width, height), paint);
        }

        // 纤维：长条状纹理，2 倍缩放平铺，看起来像纸里的长纤维
        var matrix = SKMatrix.CreateScale(2.1f, 2.1f);
        using (var shader = SKShader.CreateImage(_fiber, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, matrix))
        using (var paint = new SKPaint
               {
                   Shader = shader,
                   Color = SKColors.White.WithAlpha(120),
                   BlendMode = SKBlendMode.Multiply,
               })
        {
            canvas.DrawRect(new SKRect(0, 0, width, height), paint);
        }

        // 边缘压暗：纸张在灯下的轻微渐变
        using (var shader = SKShader.CreateRadialGradient(
                   new SKPoint(width * 0.5f, height * 0.42f),
                   Math.Max(width, height) * 0.78f,
                   [SKColors.White.WithAlpha(0), new SKColor(140, 128, 108, 110)],
                   [0.52f, 1f],
                   SKShaderTileMode.Clamp))
        using (var paint = new SKPaint { Shader = shader, BlendMode = SKBlendMode.Multiply })
        {
            canvas.DrawRect(new SKRect(0, 0, width, height), paint);
        }

        // 四边轻微压暗，做出纸的厚度
        var edge = Math.Max(24f, Math.Min(width, height) * 0.06f);
        foreach (var (x0, y0, x1, y1, r0, r1) in new[]
                 {
                     (0f, 0f, 0f, edge, 0f, 1f),
                     (0f, height - edge, 0f, height, 0f, 1f),
                 })
        {
            using var shader = SKShader.CreateLinearGradient(
                new SKPoint(x0, y0), new SKPoint(x1, y1),
                [new SKColor(150, 138, 118, 80), SKColors.White.WithAlpha(0)],
                [r0, r1], SKShaderTileMode.Clamp);
            using var paint = new SKPaint { Shader = shader, BlendMode = SKBlendMode.Multiply };
            canvas.DrawRect(new SKRect(0, y0, width, y1), paint);
        }
    }

    /// <summary>
    /// 专给大面积红色用的"吃墨不均"贴图：粗颗粒 + 几块浓墨，
    /// 放大后就是肉眼可见的深浅差，纯色块靠它才有印刷感。
    /// </summary>
    private static SKImage BuildInk(int seed)
    {
        const int size = 24;
        var rnd = new Random(seed);
        var v = new byte[size, size];
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
                v[x, y] = (byte)rnd.Next(196, 256);

        // 抹两遍，去掉噪点、留下大块浓淡
        for (var pass = 0; pass < 2; pass++)
        {
            var next = new byte[size, size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var sum = 0;
                    for (var dy = -1; dy <= 1; dy++)
                        for (var dx = -1; dx <= 1; dx++)
                            sum += v[(x + dx + size) % size, (y + dy + size) % size];
                    next[x, y] = (byte)(sum / 9);
                }
            }

            v = next;
        }

        // 平滑会把对比度压掉，这里重新拉回 188..255，保证放大后肉眼能看出来
        var lo = v.Cast<byte>().Min();
        var hi = v.Cast<byte>().Max();
        var span = Math.Max(1, hi - lo);

        using var bitmap = new SKBitmap(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var n = (byte)(166 + (v[x, y] - lo) * (255 - 166) / span);
                bitmap.SetPixel(x, y, new SKColor(n, n, n, 255));
            }
        }

        return SKImage.FromBitmap(bitmap);
    }

    private static SKImage BuildFiber(int seed)
    {
        const int size = 1024;
        using var bitmap = new SKBitmap(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        var rnd = new Random(seed);
        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke };
        for (var i = 0; i < 420; i++)
        {
            var v = (byte)rnd.Next(206, 244);
            paint.Color = new SKColor(v, v, v, 120);
            paint.StrokeWidth = (float)(rnd.NextDouble() * 1.4 + 0.4);

            var x = rnd.Next(size);
            var y = rnd.Next(size);
            var len = rnd.Next(60, 320);
            var angle = (rnd.NextDouble() - 0.5) * 0.5;
            canvas.DrawLine(x, y, x + (float)Math.Cos(angle) * len, y + (float)Math.Sin(angle) * len, paint);
        }

        return SKImage.FromBitmap(bitmap);
    }

    /// <summary>
    /// 给实心色块压纸纹。红色印在纸上会吃墨不均：低频深浅 + 纤维 + 颗粒，
    /// 三层都走乘法，纯色块才不会像塑料贴纸。
    /// </summary>
    public void Texture(SKCanvas canvas, SKRect rect, float alpha = 0.55f)
    {
        // ① 吃墨不均：粗颗粒放大，红块上最明显的一层
        using (var paint = new SKPaint
               {
                   BlendMode = SKBlendMode.Multiply,
                   Color = SKColors.White.WithAlpha((byte)(255 * Math.Min(1f, alpha * 1.2f))),
               })
        {
            canvas.DrawImage(_ink, rect, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None), paint);
        }

        // ② 纸浆斑驳
        using (var paint = new SKPaint
               {
                   BlendMode = SKBlendMode.Multiply,
                   Color = SKColors.White.WithAlpha((byte)(255 * Math.Min(1f, alpha * 0.9f))),
               })
        {
            canvas.DrawImage(_mottle, rect, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None), paint);
        }

        // ② 纤维：长条纹理，红色上最容易被看成"纸"
        var matrix = SKMatrix.CreateScale(1.7f, 1.7f);
        using (var shader = SKShader.CreateImage(_fiber, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, matrix))
        using (var paint = new SKPaint
               {
                   Shader = shader,
                   BlendMode = SKBlendMode.Multiply,
                   Color = SKColors.White.WithAlpha((byte)(255 * Math.Min(1f, alpha * 0.85f))),
               })
        {
            canvas.DrawRect(rect, paint);
        }

        // ③ 颗粒：高频，压缩后容易被抹掉，所以给足量
        using (var shader = SKShader.CreateImage(_grain, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat))
        using (var paint = new SKPaint
               {
                   Shader = shader,
                   BlendMode = SKBlendMode.Multiply,
                   Color = SKColors.White.WithAlpha((byte)(255 * Math.Min(1f, alpha * 1.1f))),
               })
        {
            canvas.DrawRect(rect, paint);
        }
    }

    public void Dispose()
    {
        _grain.Dispose();
        _mottle.Dispose();
        _fiber.Dispose();
        _ink.Dispose();
    }
}
