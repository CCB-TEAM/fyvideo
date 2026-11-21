namespace SkiaVideoLab;

/// <summary>
/// FYServer 介绍片 v3：印刷海报思维 + 相对坐标系 + 可验证的排版。
/// 与 v2 的差别：
///   1. 压在斜面色条上的文字全部在色条自己的坐标系里绘制（永远平行、永远不骑边）；
///   2. 每个绘制元素登记包围盒，交给 OverlapValidator 检查重叠 / 对比度 / 越界；
///   3. 节奏放慢到 38 秒，转场 0.5 秒；
///   4. 纸感加强：低频斑驳 + 高频颗粒 + 纤维 + 边缘压暗。
/// </summary>
public sealed class FyPosterDesign : IScene
{
    public bool Vertical { get; init; }

    public (int Width, int Height) Size => Vertical ? (1080, 1920) : (1920, 1080);

    public double Duration => 38.0;

    private readonly Typeset _type = new();
    private readonly Paper _paper = new();
    private readonly SKImage? _shot;
    private readonly List<Beat> _beats = [];

    private int _w;
    private int _h;
    private float _m;

    /// <summary>本帧的绘制登记表，验证器读它。</summary>
    public PaintLog Log { get; } = new();

    public IReadOnlyList<(double Start, double End)> Beats => _beats.Select(b => (b.Start, b.End)).ToList();

    private readonly record struct Beat(double Start, double End, Action<SKCanvas, double> Draw);

    public FyPosterDesign(string? screenshotPath = null)
    {
        var path = screenshotPath
                   ?? Environment.GetEnvironmentVariable("FY_SHOT")
                   ?? @"assets/admin-dashboard.png";
        if (File.Exists(path))
        {
            using var data = SKData.Create(path);
            using var src = SKImage.FromEncodedData(data);
            if (src != null) _shot = Duotone(src);
        }

        _beats.Add(new(0.0, 5.0, Cover));
        _beats.Add(new(5.0, 11.0, Numbers));
        _beats.Add(new(11.0, 17.5, Verbs));
        _beats.Add(new(17.5, 23.5, Spec));
        _beats.Add(new(23.5, 29.5, Console));
        _beats.Add(new(29.5, 35.0, Versus));
        _beats.Add(new(35.0, 38.0, Colophon));
    }

    public void Draw(SKCanvas canvas, double t, int frame, int width, int height)
    {
        _w = width;
        _h = height;
        _m = Vertical ? 60f : 88f;

        Log.Clear();

        _paper.DrawBackground(canvas, width, height);
        Log.Paper("纸底", new SKRect(0, 0, width, height), Swiss.Paper);

        var beat = _beats.FirstOrDefault(b => t >= b.Start && t < b.End);
        var local = beat.Draw != null ? t - beat.Start : 0;

        beat.Draw?.Invoke(canvas, local);

        Marks(canvas, t);

        // 转场：本拍结尾红板从左扫满全屏，下一拍开头再从满屏收到右侧。
        // 画面切换发生在"整屏都是红"的那一帧，所以看不到硬切。
        if (beat.Draw != null)
        {
            const double outDur = 0.5;
            const double inDur = 0.6;
            var len = beat.End - beat.Start;

            if (beat.Start > 0.01 && local < inDur)
            {
                var p = (float)Ease.InOutCubic(local / inDur);
                var edge = _w * p;
                Rect(canvas, new SKRect(edge, 0, _w, _h), Swiss.Red);
                _paper.Texture(canvas, new SKRect(edge, 0, _w, _h), 0.62f);
                Rule(canvas, edge, 0, edge, _h, 5, Swiss.Ink);
                Rule(canvas, edge - 16, 0, edge - 16, _h, 2, Swiss.Red.WithAlpha(190));
            }
            else if (beat.End < Duration - 0.01 && local > len - outDur)
            {
                var p = (float)Ease.InOutCubic((local - (len - outDur)) / outDur);
                var edge = _w * p;
                Rect(canvas, new SKRect(0, 0, edge, _h), Swiss.Red);
                _paper.Texture(canvas, new SKRect(0, 0, edge, _h), 0.62f);
                Rule(canvas, edge, 0, edge, _h, 5, Swiss.Ink);
                Rule(canvas, edge + 16, 0, edge + 16, _h, 2, Swiss.Red.WithAlpha(190));
            }
        }
    }

    // ---------------------------------------------------------------- 基础工具（带登记）

    private float H(double f) => (float)(_h * f);

    private float ContentW => _w - _m * 2;

    private SKRect SafeArea => new(_m * 0.55f, _m * 0.55f, _w - _m * 0.55f, _h - _m * 0.55f);

    /// <summary>最近一次 Draw 之后的安全区，供验证器使用。</summary>
    public SKRect CurrentSafeArea => SafeArea;

    private void T(SKCanvas c, string s, float x, float y, float size, SKColor color, float tracking = 0, bool bold = false,
        bool bleed = false)
    {
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        _type.Draw(c, s, x, y, size, paint, tracking, bold);
        Log.Text(s, _type.Bounds(s, x, y, size, tracking, bold), color, bleed);
    }

    private void TR(SKCanvas c, string s, float right, float y, float size, SKColor color, float tracking = 0,
        bool bold = false, bool bleed = false)
        => T(c, s, right - _type.Measure(s, size, tracking, bold), y, size, color, tracking, bold, bleed);

    private void Rule(SKCanvas c, float x1, float y1, float x2, float y2, float w, SKColor color)
    {
        using var paint = new SKPaint { Color = color, StrokeWidth = w, IsAntialias = true, Style = SKPaintStyle.Stroke };
        c.DrawLine(x1, y1, x2, y2, paint);
    }

    /// <summary>红色实心块：先铺色，再压一层纸纹，避免纯色"塑料感"。</summary>
    private void RedFill(SKCanvas c, SKRect r)
    {
        Rect(c, r, Swiss.Red);
        _paper.Texture(c, r, 0.8f);
    }

    private void Rect(SKCanvas c, SKRect r, SKColor color)
    {
        using var paint = new SKPaint { Color = color, IsAntialias = false };
        c.DrawRect(r, paint);
    }

    /// <summary>在色条坐标系里写字：平行于色条，并按旋转后的包围盒登记。</summary>
    private void BandText(SKCanvas c, Band band, string s, float lx, float ly, float size, SKColor color,
        float tracking = 0, bool bold = false, bool bleed = false)
    {
        var local = _type.Bounds(s, lx, ly, size, tracking, bold);
        band.Frame(c, cc =>
        {
            using var paint = new SKPaint { Color = color, IsAntialias = true };
            _type.Draw(cc, s, lx, ly, size, paint, tracking, bold);
        });
        Log.Text(s, band.ToCanvas(local), color, bleed, band.Poly(local));
    }

    /// <summary>绕任意点旋转写字，包围盒按旋转后登记。</summary>
    private void RotText(SKCanvas c, string s, float x, float y, float size, SKColor color, float angle, SKPoint pivot,
        float tracking = 0, bool bold = false, bool bleed = false)
    {
        var bounds = _type.Bounds(s, x, y, size, tracking, bold);
        c.Save();
        c.RotateDegrees(angle, pivot.X, pivot.Y);
        using (var paint = new SKPaint { Color = color, IsAntialias = true })
            _type.Draw(c, s, x, y, size, paint, tracking, bold);
        c.Restore();
        Log.Text(s, RotatedAabb(bounds, angle, pivot), color, bleed, RotPoly(bounds, angle, pivot));
    }

    private static SKPoint[] RotPoly(SKRect r, float angle, SKPoint pivot)
    {
        var rad = angle * MathF.PI / 180f;
        var cos = MathF.Cos(rad);
        var sin = MathF.Sin(rad);
        var pts = new[] { (r.Left, r.Top), (r.Right, r.Top), (r.Right, r.Bottom), (r.Left, r.Bottom) };
        var result = new SKPoint[4];
        for (var i = 0; i < 4; i++)
        {
            var dx = pts[i].Item1 - pivot.X;
            var dy = pts[i].Item2 - pivot.Y;
            result[i] = new SKPoint(pivot.X + dx * cos - dy * sin, pivot.Y + dx * sin + dy * cos);
        }

        return result;
    }

    private static SKRect RotatedAabb(SKRect r, float angle, SKPoint pivot)
    {
        var rad = angle * MathF.PI / 180f;
        var cos = MathF.Cos(rad);
        var sin = MathF.Sin(rad);

        var xs = new float[4];
        var ys = new float[4];
        var pts = new[] { (r.Left, r.Top), (r.Right, r.Top), (r.Right, r.Bottom), (r.Left, r.Bottom) };
        for (var i = 0; i < 4; i++)
        {
            var dx = pts[i].Item1 - pivot.X;
            var dy = pts[i].Item2 - pivot.Y;
            xs[i] = pivot.X + dx * cos - dy * sin;
            ys[i] = pivot.Y + dx * sin + dy * cos;
        }

        return new SKRect(xs.Min(), ys.Min(), xs.Max(), ys.Max());
    }

    // ---------------------------------------------------------------- 色条坐标系

    /// <summary>
    /// 一条斜置色条的局部坐标系。压在上面的文字都在这里画，
    /// 因此永远与色条平行、永远落在色条内部。
    /// </summary>
    private readonly record struct Band(SKPoint Center, float Angle, float HalfLength, float HalfThickness)
    {
        /// <summary>进入色条坐标系：原点在色条中心，x 沿色条方向。</summary>
        public void Frame(SKCanvas c, Action<SKCanvas> draw)
        {
            c.Save();
            c.Translate(Center.X, Center.Y);
            c.RotateDegrees(Angle);
            draw(c);
            c.Restore();
        }

        /// <summary>局部矩形 → 画布 AABB。</summary>
        public SKRect ToCanvas(SKRect local)
        {
            var rotated = RotatedAabb(local, Angle, SKPoint.Empty);
            return new SKRect(rotated.Left + Center.X, rotated.Top + Center.Y,
                rotated.Right + Center.X, rotated.Bottom + Center.Y);
        }

        public SKRect Aabb(float halfLength) => ToCanvas(new SKRect(-halfLength, -HalfThickness, halfLength, HalfThickness));

        /// <summary>局部矩形 → 画布上的四个角（验证器要精确多边形）。</summary>
        public SKPoint[] Poly(SKRect local)
        {
            var rad = Angle * MathF.PI / 180f;
            var cos = MathF.Cos(rad);
            var sin = MathF.Sin(rad);
            var pts = new[] { (local.Left, local.Top), (local.Right, local.Top), (local.Right, local.Bottom), (local.Left, local.Bottom) };
            var result = new SKPoint[4];
            for (var i = 0; i < 4; i++)
                result[i] = new SKPoint(Center.X + pts[i].Item1 * cos - pts[i].Item2 * sin,
                    Center.Y + pts[i].Item1 * sin + pts[i].Item2 * cos);
            return result;
        }
    }

    // ---------------------------------------------------------------- 角规与刻度

    private void Marks(SKCanvas c, double t)
    {
        var inset = _m * 0.42f;
        var len = _m * 0.28f;
        var hair = Swiss.Ink.WithAlpha(110);

        foreach (var (x, y, sx, sy) in new[]
                 {
                     (inset, inset, 1f, 1f),
                     (_w - inset, inset, -1f, 1f),
                     (inset, _h - inset, 1f, -1f),
                     (_w - inset, _h - inset, -1f, -1f),
                 })
        {
            Rule(c, x, y, x + len * sx, y, 1.2f, hair);
            Rule(c, x, y, x, y + len * sy, 1.2f, hair);
        }

        for (var x = _m; x <= _w - _m; x += 48)
        {
            var longTick = (int)Math.Round((x - _m) / 48) % 5 == 0;
            Rule(c, x, _m * 0.40f, x, _m * 0.40f + (longTick ? 15 : 7), 1, Swiss.Ink.WithAlpha(85));
        }

        var cx = _w - _m - 110 + (float)Math.Sin(t * 0.9) * 2;
        var cy = _m + 92 + (float)Math.Cos(t * 0.7) * 2;
        using (var paint = new SKPaint { Color = Swiss.Red.WithAlpha(200), StrokeWidth = 1.4f, IsAntialias = true, Style = SKPaintStyle.Stroke })
        {
            c.DrawCircle(cx, cy, 11, paint);
            c.DrawLine(cx - 18, cy, cx + 18, cy, paint);
            c.DrawLine(cx, cy - 18, cx, cy + 18, paint);
        }

        var index = _beats.FindIndex(b => t >= b.Start && t < b.End);
        if (index < 0) index = _beats.Count - 1;
        TR(c, $"{index + 1:00}—{_beats.Count:00}", _w - _m, _h - _m * 0.45f, 13, Swiss.Ink.WithAlpha(150), 2.2f, true, bleed: true);
    }

    // ---------------------------------------------------------------- 双色网点

    private static SKImage Duotone(SKImage src)
    {
        var info = new SKImageInfo(src.Width, src.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(Swiss.Paper);

        var dark = Swiss.Ink;
        var light = new SKColor(0xFF, 0xFC, 0xF2);
        const float blackPoint = 152f;
        var span = 255f - blackPoint;

        var sr = (light.Red - dark.Red) / span;
        var sg = (light.Green - dark.Green) / span;
        var sb = (light.Blue - dark.Blue) / span;

        var offR = (dark.Red - (light.Red - dark.Red) * (blackPoint / span)) / 255f;
        var offG = (dark.Green - (light.Green - dark.Green) * (blackPoint / span)) / 255f;
        var offB = (dark.Blue - (light.Blue - dark.Blue) * (blackPoint / span)) / 255f;

        var matrix = new float[]
        {
            sr * 0.299f, sr * 0.587f, sr * 0.114f, 0, offR,
            sg * 0.299f, sg * 0.587f, sg * 0.114f, 0, offG,
            sb * 0.299f, sb * 0.587f, sb * 0.114f, 0, offB,
            0, 0, 0, 1, 0,
        };

        using (var filter = SKColorFilter.CreateColorMatrix(matrix))
        using (var paint = new SKPaint { ColorFilter = filter, IsAntialias = true })
        {
            canvas.DrawImage(src, new SKRect(0, 0, src.Width, src.Height),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint);
        }

        using (var paint = new SKPaint { BlendMode = SKBlendMode.Multiply, Color = SKColors.White.WithAlpha(70), IsAntialias = true })
        {
            canvas.DrawImage(src, new SKRect(0, 0, src.Width, src.Height),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint);
        }

        using (var dots = DotTile())
        using (var shader = SKShader.CreateImage(dots, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat))
        using (var paint = new SKPaint { Shader = shader, BlendMode = SKBlendMode.Multiply, Color = SKColors.White.WithAlpha(60) })
        {
            canvas.DrawRect(new SKRect(0, 0, src.Width, src.Height), paint);
        }

        return surface.Snapshot();
    }

    private static SKImage DotTile()
    {
        const int n = 5;
        using var bmp = new SKBitmap(n, n, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.White);
        using var paint = new SKPaint { Color = new SKColor(120, 110, 96, 190), IsAntialias = true };
        canvas.DrawCircle(n / 2f, n / 2f, 1.35f, paint);
        return SKImage.FromBitmap(bmp);
    }

    // ---------------------------------------------------------------- 01 封面

    private void Cover(SKCanvas c, double u)
    {
        var nameIn = Ease.OutCubic(Ease.Ramp(u, 0.9, 1.8));
        var bandIn = Ease.OutQuint(Ease.Ramp(u, 0.2, 1.2));

        if (Vertical)
        {
            var band = new Band(new SKPoint(_w * 0.5f, H(0.545)), -6 + (float)(u * 0.2), _w * 1.35f, 92f);

            band.Frame(c, cc =>
            {
                var w = band.HalfLength * (float)bandIn;
                RedFill(cc, new SKRect(-w, -band.HalfThickness, w, band.HalfThickness));
            });
            Log.Decor("红条", band.Aabb(band.HalfLength * (float)bandIn), Swiss.Red,
                new BandShape(band.Center, band.Angle, band.HalfLength * (float)bandIn, band.HalfThickness));

            T(c, "FY", -14, H(0.36), 500, Swiss.Ink, -18, true, bleed: true);

            var serverY = H(0.36) + 118;
            T(c, "SERVER", _m - 4, serverY, 86, Swiss.Ink, 40 - 24 * (float)nameIn, true);

            var leftV = -492f;
            BandText(c, band, "KARDS 私服后端", leftV, 24, 40, Swiss.Paper, 0, true);
            BandText(c, band, "从 Gin 到 ASP.NET Core 的一次重写", leftV, 172, 24, Swiss.Ink.WithAlpha(205));

            return;
        }

        var bandL = new Band(new SKPoint(760, 812), -5 + (float)(u * 0.2), 1560f, 100f);

        bandL.Frame(c, cc =>
        {
            var w = bandL.HalfLength * (float)bandIn;
            RedFill(cc, new SKRect(-w, -bandL.HalfThickness, w, bandL.HalfThickness));
        });
        Log.Decor("红条", bandL.Aabb(bandL.HalfLength * (float)bandIn), Swiss.Red,
            new BandShape(bandL.Center, bandL.Angle, bandL.HalfLength * (float)bandIn, bandL.HalfThickness));

        var fy = 700f;
        T(c, "FY", -30, 616, fy, Swiss.Ink, -22, true, bleed: true);

        var fyW = _type.Measure("FY", fy, -22, true);
        T(c, "SERVER", -30 + fyW + 44, 616, 110, Swiss.Ink, 52 - 30 * (float)nameIn, true);

        var leftL = -644f;
        BandText(c, bandL, "KARDS 私服后端", leftL, 30, 46, Swiss.Paper, 0, true);
        BandText(c, bandL, "从 Gin 到 ASP.NET Core 的一次重写", leftL, 152, 24, Swiss.Ink.WithAlpha(205));
    }

    // ---------------------------------------------------------------- 02 数字

    private void Numbers(SKCanvas c, double u)
    {
        var items = new (string Value, string Caption)[]
        {
            ("24", "MB · 单文件 AOT"),
            ("0", "预装运行时"),
            ("1", "端口 HTTP + WS"),
        };

        if (Vertical)
        {
            var band = new Band(new SKPoint(_w * 0.9f, H(0.5)), -6, 520f, 200f);
            band.Frame(c, cc =>
            {
                using var stroke = new SKPaint
                {
                    Color = Swiss.Red, StrokeWidth = 12, Style = SKPaintStyle.Stroke, IsAntialias = true,
                };
                cc.DrawRect(new SKRect(-band.HalfLength, -band.HalfThickness, band.HalfLength, band.HalfThickness), stroke);
            });

            BandText(c, band, "3×", -band.HalfLength + 54, 30, 150, Swiss.Red, -6, true);

            for (var i = 0; i < items.Length; i++)
            {
                var p = Ease.OutQuint(Ease.Ramp(u, 0.3 + i * 0.3, 1.1 + i * 0.3));
                if (p <= 0.001) continue;

                var y = H(0.29 + i * 0.21);
                T(c, items[i].Value, _m - 10, y, 250, Swiss.Ink, -10, true);
                T(c, items[i].Caption, _m - 6, y + 58, 32, Swiss.Ink.WithAlpha(225), 0, true);
                Rule(c, _m - 6, y + 78, _m + 560 * (float)p, y + 78, 2, Swiss.Ink.WithAlpha(140));
            }

            return;
        }

        // 右侧大红块 → 粗描边"框选"，不填色；框里放 3× 与注脚，内边距 64
        var bandL = new Band(new SKPoint(_w + 80, _h * 0.5f), -4, _h * 0.8f, 250f);
        bandL.Frame(c, cc =>
        {
            using var stroke = new SKPaint
            {
                Color = Swiss.Red, StrokeWidth = 14, Style = SKPaintStyle.Stroke, IsAntialias = true,
            };
            cc.DrawRect(new SKRect(-bandL.HalfLength, -bandL.HalfThickness, bandL.HalfLength, bandL.HalfThickness), stroke);
        });

        BandText(c, bandL, "3×", -bandL.HalfLength + 64, 58, 186, Swiss.Red, -8, true);
        BandText(c, bandL, "SELECTED · 24 / 0 / 1", -bandL.HalfLength + 70, 112, 21, Swiss.Red.WithAlpha(225), 2.4f, true);

        var pos = new[] { (X: 96f, Y: 420f), (X: 430f, Y: 660f), (X: 764f, Y: 900f) };
        for (var i = 0; i < items.Length; i++)
        {
            var p = Ease.OutQuint(Ease.Ramp(u, 0.3 + i * 0.3, 1.1 + i * 0.3));
            if (p <= 0.001) continue;

            var lag = 30 * (1 - (float)p) + 6;
            using (var red = new SKPaint { Color = Swiss.Red.WithAlpha(215), IsAntialias = true, BlendMode = SKBlendMode.Multiply })
                _type.Draw(c, items[i].Value, pos[i].X + lag, pos[i].Y - lag * 0.5f, 290, red, -12, true);

            T(c, items[i].Value, pos[i].X, pos[i].Y, 290, Swiss.Ink, -12, true);
            T(c, items[i].Caption, pos[i].X + 4, pos[i].Y + 60, 34, Swiss.Ink.WithAlpha(225), 0, true);
            Rule(c, pos[i].X + 4, pos[i].Y + 80, pos[i].X + 4 + 560 * (float)p, pos[i].Y + 80, 2, Swiss.Ink.WithAlpha(140));
        }


    }

    // ---------------------------------------------------------------- 03 六个动词

    private void Verbs(SKCanvas c, double u)
    {
        var words = new (string Cn, string En)[]
        {
            ("登录", "SESSION"),
            ("卡组", "DECKS"),
            ("商店", "STORE"),
            ("对局", "MATCH"),
            ("重连", "RECONNECT"),
            ("后台", "ADMIN"),
        };

        var drift = (float)(-20 * Ease.InOutCubic(Ease.Ramp(u, 0, 6.5)));

        if (Vertical)
        {
            var rowH = H(0.105);
            var start = H(0.235) + drift;

            for (var i = 0; i < words.Length; i++)
            {
                var p = Ease.OutQuint(Ease.Ramp(u, 0.25 + i * 0.18, 0.95 + i * 0.18));
                var y = start + i * rowH;
                Rule(c, _m, y - rowH * 0.62f, _m + ContentW * 0.94f * (float)p, y - rowH * 0.62f, 1.4f, Swiss.Ink.WithAlpha(120));

                if (p <= 0.001) continue;
                T(c, $"{i + 1:00}", _m, y, 15, Swiss.Red, 2.2f, true);
                T(c, words[i].Cn, _m + 52, y, 76, Swiss.Ink, -2, true);
                TR(c, words[i].En, _w - _m, y, 14, Swiss.Ink.WithAlpha(165), 2.6f, true);
            }

            return;
        }

        var startL = 310f + drift;
        for (var i = 0; i < words.Length; i++)
        {
            var p = Ease.OutQuint(Ease.Ramp(u, 0.25 + i * 0.18, 0.95 + i * 0.18));
            var y = startL + i * 112;
            Rule(c, 96, y - 60, 96 + 1640 * (float)p, y - 60, 2f, Swiss.Ink.WithAlpha(135));

            if (p <= 0.001) continue;
            T(c, $"{i + 1:00}", 96, y, 17, Swiss.Red, 2.6f, true);
            T(c, words[i].Cn, 200, y, 92, Swiss.Ink, -3, true);
            TR(c, words[i].En, 1700, y, 15, Swiss.Ink.WithAlpha(165), 3.2f, true);
        }

        RotText(c, "SIX THINGS", _w - _m - 6, 1010, 108, Swiss.Red, -90, new SKPoint(_w - _m - 6, 1010), 4, true);
    }

    // ---------------------------------------------------------------- 04 技术规格

    private void Spec(SKCanvas c, double u)
    {
        var rows = new (string Title, string Note)[]
        {
            ("FASTER KV", "username / id 双索引 · 检查点落盘"),
            ("源生成 JSON", "全量零反射 · AOT 与裁剪都能带"),
            ("HTTP + WS", "同端口同管线 · 路径自动归一化"),
            ("纯 C# 编解码", "Base64 + XOR 查表 · 无原生 DLL"),
        };

        if (Vertical)
        {
            var rowH = H(0.145);
            var start = H(0.24);

            for (var i = 0; i < rows.Length; i++)
            {
                var p = Ease.OutQuint(Ease.Ramp(u, 0.25 + i * 0.2, 0.95 + i * 0.2));
                var y = start + i * rowH;
                Rule(c, _m, y - rowH * 0.6f, _m + ContentW * 0.9f * (float)p, y - rowH * 0.6f, 1.4f, Swiss.Ink.WithAlpha(110));

                if (p <= 0.001) continue;
                T(c, $"{i + 1:00}", _m, y - 6, 14, Swiss.Red, 2.2f, true);
                T(c, rows[i].Title, _m + 46, y + 26, 42, Swiss.Ink, -1.5f, true);
                T(c, rows[i].Note, _m + 46, y + 62, 21, Swiss.Ink.WithAlpha(185));
            }

            return;
        }

        T(c, "AOT", 1090, 168, 290, Swiss.Red.WithAlpha(55), -10, true, bleed: true);

        var startL = 300f;
        for (var i = 0; i < rows.Length; i++)
        {
            var p = Ease.OutQuint(Ease.Ramp(u, 0.25 + i * 0.2, 0.95 + i * 0.2));
            var y = startL + i * 132;
            Rule(c, 96, y - 66, 96 + 1500 * (float)p, y - 66, 1.4f, Swiss.Ink.WithAlpha(110));

            if (p <= 0.001) continue;
            T(c, $"{i + 1:00}", 96, y - 8, 15, Swiss.Red, 2.4f, true);
            T(c, rows[i].Title, 170, y + 20, 58, Swiss.Ink, -2, true);
            TR(c, rows[i].Note, 1600, y + 20, 23, Swiss.Ink.WithAlpha(185));
        }
    }

    // ---------------------------------------------------------------- 05 后台

    private void Console(SKCanvas c, double u)
    {
        var p = Ease.OutQuint(Ease.Ramp(u, 0.2, 1.3));

        if (Vertical)
        {
            var panel = new SKRect(_m - 20, H(0.185), _w + 40, H(0.185) + (_w - _m) * 0.58f);
            Shot(c, panel, -2.2f, p);

            T(c, "内建后台", _m, H(0.60), 62, Swiss.Ink, -2, true);
            T(c, "Vue 3 · 账号 / 权限 / 审计 / 配置热更新", _m, H(0.60) + 46, 24, Swiss.Ink.WithAlpha(200));

            var notes = new[] { "封禁 / 踢线 / 改钱包", "服务器配置逐项开关", "对局监控与历史回放" };
            for (var i = 0; i < notes.Length; i++)
            {
                var q = Ease.OutCubic(Ease.Ramp(u, 1.1 + i * 0.2, 1.8 + i * 0.2));
                if (q <= 0.001) continue;
                var y = H(0.68) + i * 58;
                Rect(c, new SKRect(_m, y - 16, _m + 14, y - 2), Swiss.Red);
                T(c, notes[i], _m + 30, y, 28, Swiss.Ink);
            }

            return;
        }

        var panelL = new SKRect(840, 170, _w + 120, 950);
        Shot(c, panelL, -2.4f, p);

        T(c, "内建后台", 96, 300, 62, Swiss.Ink, -2, true);
        T(c, "Vue 3 · 权限 · 审计 · 配置热更新", 96, 346, 23, Swiss.Ink.WithAlpha(200));

        var notesL = new[] { "封禁 / 踢线 / 改钱包", "服务器配置逐项开关", "对局监控与历史回放" };
        for (var i = 0; i < notesL.Length; i++)
        {
            var q = Ease.OutCubic(Ease.Ramp(u, 1.0 + i * 0.2, 1.7 + i * 0.2));
            if (q <= 0.001) continue;
            var y = 470 + i * 58;
            Rect(c, new SKRect(96, y - 16, 110, y - 2), Swiss.Red);
            T(c, notesL[i], 126, y, 27, Swiss.Ink);
        }

        RotText(c, "ADMIN", 780, 920, 160, Swiss.Ink, -90, new SKPoint(780, 920), -6, true);
    }

    /// <summary>斜置的双色截图：墨线边框 + 红色衬底。</summary>
    private void Shot(SKCanvas c, SKRect panel, float angle, double p)
    {
        if (p <= 0.001) return;

        var slide = (float)((1 - p) * 280);
        var moved = new SKRect(panel.Left + slide, panel.Top, panel.Right + slide, panel.Bottom);
        var pivot = new SKPoint(panel.MidX, panel.MidY);

        c.Save();
        c.RotateDegrees(angle, pivot.X, pivot.Y);

        Rect(c, new SKRect(moved.Left - 24, moved.Top - 24, moved.Right - 24, moved.Bottom - 24), Swiss.Red);

        c.Save();
        c.ClipRect(moved);
        Rect(c, moved, Swiss.Paper);

        if (_shot != null)
        {
            const float zoom = 1.9f;
            var srcW = _shot.Width / zoom;
            var srcH = _shot.Height / zoom;
            var srcRect = new SKRect(0, 0, srcW, srcH);
            var scale = Math.Max(moved.Width / srcW, moved.Height / srcH);
            var dest = new SKRect(moved.Left, moved.Top, moved.Left + srcW * scale, moved.Top + srcH * scale);

            using var paint = new SKPaint { IsAntialias = true };
            c.DrawImage(_shot, srcRect, dest, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint);
        }

        c.Restore();

        using var border = new SKPaint { Color = Swiss.Ink, StrokeWidth = 4, IsAntialias = true, Style = SKPaintStyle.Stroke };
        c.DrawRect(moved, border);

        c.Restore();

        Log.Panel("后台截图", RotatedAabb(moved, angle, pivot), Swiss.Paper);
    }

    // ---------------------------------------------------------------- 06 对比

    private void Versus(SKCanvas c, double u)
    {
        var p = Ease.OutQuint(Ease.Ramp(u, 0.15, 1.1));

        // 两边的特点各自成栏：大标题 → 语言 → 三条特征，各自贴在自己那半幅上。
        var mine = new[] { "24 MB 单文件 AOT", "Vue 3 后台 · 权限审计", "FASTER KV / MySQL / PG" };
        var theirs = new[] { "Go 编译产物", "静态 HTML 后台", "GORM · SQLite / MySQL" };

        if (Vertical)
        {
            using (var path = new SKPath())
            {
                path.MoveTo(0, H(0.56));
                path.LineTo(_w, H(0.47));
                path.LineTo(_w, H(1));
                path.LineTo(0, H(1));
                path.Close();

                c.Save();
                c.ClipRect(new SKRect(0, H(0.47), _w, H(0.47) + (H(1) - H(0.47)) * (float)p));
                using var paint = new SKPaint { Color = Swiss.Red, IsAntialias = true };
                c.DrawPath(path, paint);
                c.Restore();
            }

            Log.Decor("红切块", new SKRect(0, H(0.47), _w, H(1)), Swiss.Red);

            // 上半幅（纸）：FYServer 及其特征
            T(c, "FYServer", _m, H(0.28), 92, Swiss.Ink, -3, true);
            T(c, "C# · ASP.NET Core", _m, H(0.28) + 44, 24, Swiss.Ink.WithAlpha(200));
            for (var i = 0; i < mine.Length; i++)
            {
                var q = Ease.OutCubic(Ease.Ramp(u, 0.7 + i * 0.16, 1.3 + i * 0.16));
                if (q <= 0.001) continue;
                Rule(c, _m, H(0.28) + 72 + i * 40, _m + 420 * (float)q, H(0.28) + 72 + i * 40, 1.4f, Swiss.Ink.WithAlpha(110));
                T(c, mine[i], _m, H(0.28) + 108 + i * 40, 24, Swiss.Ink.WithAlpha(215));
            }

            // 下半幅（红）：kards-server-go 及其特征
            T(c, "kards-server-go", _m, H(0.60), 68, Swiss.Paper, -2, true);
            T(c, "Go · Gin", _m, H(0.60) + 40, 24, Swiss.Paper.WithAlpha(220));
            for (var i = 0; i < theirs.Length; i++)
            {
                var q = Ease.OutCubic(Ease.Ramp(u, 1.1 + i * 0.16, 1.7 + i * 0.16));
                if (q <= 0.001) continue;
                Rule(c, _m, H(0.60) + 66 + i * 40, _m + 420 * (float)q, H(0.60) + 66 + i * 40, 1.4f, Swiss.Paper.WithAlpha(150));
                T(c, theirs[i], _m, H(0.60) + 102 + i * 40, 24, Swiss.Paper.WithAlpha(230));
            }

            return;
        }

        // 分镜式斜切：一条贯穿全幅的斜带，20°，占比大到能当版面分割线
        var split = new Band(new SKPoint(_w * 0.5f, _h * 1.16f), -20, _w * 0.75f, _h * 0.57f);
        split.Frame(c, cc =>
        {
            var half = split.HalfLength * (float)p;
            RedFill(cc, new SKRect(-half, -split.HalfThickness, half, split.HalfThickness));
        });
        Log.Decor("红切块", split.Aabb(split.HalfLength * (float)p), Swiss.Red,
            new BandShape(split.Center, split.Angle, split.HalfLength * (float)p, split.HalfThickness));

        // 左半幅（纸）：FYServer + 语言 + 三条特征，整块跟斜边同角度
        var leftPivot = new SKPoint(150, 400);
        RotText(c, "FYServer", 150, 400, 118, Swiss.Ink, -20, leftPivot, -5, true);
        RotText(c, "C# · ASP.NET Core", 152, 446, 24, Swiss.Ink.WithAlpha(200), -20, leftPivot);
        for (var i = 0; i < mine.Length; i++)
        {
            var q = Ease.OutCubic(Ease.Ramp(u, 0.8 + i * 0.16, 1.4 + i * 0.16));
            if (q <= 0.001) continue;
            RotText(c, mine[i], 152, 494 + i * 36, 22, Swiss.Ink.WithAlpha(215), -20, leftPivot);
        }

        // 右半幅（红）：kards-server-go + 语言 + 三条特征，同样跟斜带走
        BandText(c, split, "kards-server-go", 299, -412, 80, Swiss.Paper, -2, true);
        BandText(c, split, "Go · Gin", 301, -370, 22, Swiss.Paper.WithAlpha(220));
        for (var i = 0; i < theirs.Length; i++)
        {
            var q = Ease.OutCubic(Ease.Ramp(u, 1.2 + i * 0.16, 1.8 + i * 0.16));
            if (q <= 0.001) continue;
            BandText(c, split, theirs[i], 301, -334 + i * 34, 22, Swiss.Paper.WithAlpha(230));
        }
    }
    // ---------------------------------------------------------------- 07 落版

    private void Colophon(SKCanvas c, double u)
    {
        var p = Ease.OutQuint(Ease.Ramp(u, 0.1, 1.0));
        var flood = _h * (Vertical ? 0.40f : 0.46f);
        var top = _h - flood;

        c.Save();
        c.ClipRect(new SKRect(0, top, _w, top + flood * (float)p));
        RedFill(c, new SKRect(0, top, _w, _h + 10));
        c.Restore();

        Log.Decor("红潮", new SKRect(0, top, _w, _h), Swiss.Red);

        if (Vertical)
        {
            T(c, "现在就能跑", _m, top - 130, 58, Swiss.Ink, -2, true);
            T(c, "dotnet publish", _m, top - 76, 26, Swiss.Ink.WithAlpha(200));

            T(c, "MIT", _m - 10, _h - 50, 290, Swiss.Paper, -12, true, bleed: true);
            TR(c, "CCB-TEAM / FYSERVER", _w - _m, top + 70, 14, Swiss.Paper.WithAlpha(225), 2.8f, true);
            T(c, "github.com/CCB-TEAM/fyserver", _m, top + 128, 30, Swiss.Paper, 0, true);
            return;
        }

        T(c, "现在就能跑", 96, top - 118, 62, Swiss.Ink, -2, true);
        T(c, "dotnet publish 一条命令 · 24 MB 服务器", 96, top - 66, 27, Swiss.Ink.WithAlpha(200));

        T(c, "MIT", -12, _h - 6, 360, Swiss.Paper, -14, true, bleed: true);
        TR(c, "CCB-TEAM / FYSERVER", _w - _m, top + 80, 14, Swiss.Paper.WithAlpha(225), 3, true);
        T(c, "github.com/CCB-TEAM/fyserver", 500, top + 136, 34, Swiss.Paper, 0, true);
    }
}
