namespace SkiaVideoLab;

/// <summary>绘制层：决定谁压在谁上面。</summary>
public enum PaintLayer
{
    Paper,
    Decor,
    Panel,
    Text,
}

/// <summary>斜置色条的精确形状，用来做"是否真的落在色条内"的判定。</summary>
public sealed record BandShape(SKPoint Center, float Angle, float HalfLength, float HalfThickness);

/// <summary>
/// 一次绘制的记录。除了 AABB，还带四个角的实际多边形——
/// 斜排文字的 AABB 会大得离谱，只有多边形才判得准。
/// </summary>
public sealed record PaintBox(
    string Label,
    SKRect Rect,
    SKColor Color,
    PaintLayer Layer,
    bool Bleed = false,
    BandShape? Shape = null,
    SKPoint[]? Poly = null);

/// <summary>记录每一帧画了哪些东西。只为验证服务。</summary>
public sealed class PaintLog
{
    public List<PaintBox> Items { get; } = new();

    public bool Enabled { get; set; } = true;

    public void Clear() => Items.Clear();

    public void Add(PaintBox box)
    {
        if (Enabled) Items.Add(box);
    }

    public void Text(string label, SKRect rect, SKColor color, bool bleed = false, SKPoint[]? poly = null)
        => Add(new PaintBox(Short(label), rect, color, PaintLayer.Text, bleed, null, poly ?? Corners(rect)));

    public void Decor(string label, SKRect rect, SKColor color, BandShape? shape = null)
        => Add(new PaintBox(Short(label), rect, color, PaintLayer.Decor, false, shape, Corners(rect)));

    public void Panel(string label, SKRect rect, SKColor color)
        => Add(new PaintBox(Short(label), rect, color, PaintLayer.Panel, false, null, Corners(rect)));

    public void Paper(string label, SKRect rect, SKColor color)
        => Add(new PaintBox(Short(label), rect, color, PaintLayer.Paper, false, null, Corners(rect)));

    public static SKPoint[] Corners(SKRect r) =>
        [new SKPoint(r.Left, r.Top), new SKPoint(r.Right, r.Top), new SKPoint(r.Right, r.Bottom), new SKPoint(r.Left, r.Bottom)];

    private static string Short(string s) => s.Length <= 28 ? s : s[..27] + "…";
}

/// <summary>
/// 重叠验证器：只允许"期望范围内"的重叠。
///   R1 文字之间不得重叠（多边形求交，斜排文字也准）；
///   R2 文字不得骑在色块边缘（斜置色条按坐标系判定）；
///   R3 文字压在色块上时对比度 WCAG ≥ 3.0；
///   R4 非出血元素不得越过安全区。
/// </summary>
public static class OverlapValidator
{
    public sealed record Violation(string Rule, string Detail);

    public static List<Violation> Analyze(IReadOnlyList<PaintBox> items, SKRect safeArea)
    {
        var violations = new List<Violation>();
        var texts = items.Where(i => i.Layer == PaintLayer.Text).ToList();
        var fields = items.Where(i => i.Layer is PaintLayer.Decor or PaintLayer.Panel).ToList();

        // R1 文字互相重叠
        for (var i = 0; i < texts.Count; i++)
        {
            for (var j = i + 1; j < texts.Count; j++)
            {
                var a = texts[i].Poly;
                var b = texts[j].Poly;
                if (a == null || b == null) continue;
                if (SKRect.Intersect(texts[i].Rect, texts[j].Rect).IsEmpty) continue;

                var ratio = OverlapRatio(a, b);
                if (ratio > 0.15f)
                    violations.Add(new("R1 文字重叠", $"「{texts[i].Label}」×「{texts[j].Label}」 重叠 {ratio:P0}"));
            }
        }

        foreach (var text in texts)
        {
            var core = Shrink(text.Rect, 3);

            // R4 安全区
            if (!text.Bleed && !Contains(safeArea, core))
                violations.Add(new("R4 越界", $"「{text.Label}」超出安全区"));

            // R2 骑线 + R3 对比度
            PaintBox? under = null;

            foreach (var field in fields)
            {
                if (SKRect.Intersect(core, field.Rect).IsEmpty) continue;

                var poly = text.Poly ?? PaintLog.Corners(core);

                var (inside, straddle) = field.Shape is { } shape
                    ? TestOriented(poly, shape)
                    : (Contains(field.Rect, core), OverlapRatio(poly, field.Poly ?? PaintLog.Corners(field.Rect)) > 0.03f);

                if (inside)
                {
                    under = field;
                }
                else if (straddle && !text.Bleed)
                {
                    violations.Add(new("R2 骑线", $"「{text.Label}」压在「{field.Label}」边缘"));
                }
            }

            if (under != null)
            {
                var ratio = Contrast(text.Color, under.Color);
                if (ratio < 3.0)
                    violations.Add(new("R3 低对比", $"「{text.Label}」在「{under.Label}」上仅 {ratio:F2}:1"));
            }
        }

        return violations;
    }

    /// <summary>把多边形变换到色条坐标系，判断完全在内 / 完全在外 / 骑边。</summary>
    private static (bool Inside, bool Straddle) TestOriented(SKPoint[] poly, BandShape s)
    {
        var rad = -s.Angle * MathF.PI / 180f;
        var cos = MathF.Cos(rad);
        var sin = MathF.Sin(rad);

        var insideCount = 0;
        foreach (var p in poly)
        {
            var dx = p.X - s.Center.X;
            var dy = p.Y - s.Center.Y;
            var lx = dx * cos - dy * sin;
            var ly = dx * sin + dy * cos;
            if (Math.Abs(lx) <= s.HalfLength + 1 && Math.Abs(ly) <= s.HalfThickness + 1) insideCount++;
        }

        if (insideCount == poly.Length) return (true, false);
        if (insideCount == 0) return (false, false);
        return (false, true);
    }

    /// <summary>两个凸多边形是否相交（分离轴定理）。</summary>
    private static bool PolyIntersects(SKPoint[] a, SKPoint[] b)
    {
        foreach (var poly in new[] { a, b })
        {
            for (var i = 0; i < poly.Length; i++)
            {
                var p1 = poly[i];
                var p2 = poly[(i + 1) % poly.Length];
                var axisX = -(p2.Y - p1.Y);
                var axisY = p2.X - p1.X;

                var (minA, maxA) = Project(a, axisX, axisY);
                var (minB, maxB) = Project(b, axisX, axisY);
                if (maxA < minB || maxB < minA) return false;
            }
        }

        return true;
    }

    private static (float Min, float Max) Project(SKPoint[] poly, float ax, float ay)
    {
        var min = float.MaxValue;
        var max = float.MinValue;
        foreach (var p in poly)
        {
            var v = p.X * ax + p.Y * ay;
            min = Math.Min(min, v);
            max = Math.Max(max, v);
        }

        return (min, max);
    }

    /// <summary>重叠面积占比：在公共 AABB 上采样，够准也够快。</summary>
    private static float OverlapRatio(SKPoint[] a, SKPoint[] b)
    {
        if (!PolyIntersects(a, b)) return 0f;

        var ab = Aabb(a);
        var bb = Aabb(b);
        var inter = SKRect.Intersect(ab, bb);
        if (inter.IsEmpty || inter.Width <= 0 || inter.Height <= 0) return 0f;

        const int n = 14;
        var inside = 0;
        for (var iy = 0; iy < n; iy++)
        {
            for (var ix = 0; ix < n; ix++)
            {
                var x = inter.Left + inter.Width * (ix + 0.5f) / n;
                var y = inter.Top + inter.Height * (iy + 0.5f) / n;
                if (Inside(a, x, y) && Inside(b, x, y)) inside++;
            }
        }

        var union = Math.Min(Area(ab), Area(bb));
        return union <= 0 ? 0 : inside / (float)(n * n) * (inter.Width * inter.Height) / union;
    }

    private static float Area(SKRect r) => r.Width * r.Height;

    private static SKRect Aabb(SKPoint[] poly)
    {
        var minX = poly.Min(p => p.X);
        var minY = poly.Min(p => p.Y);
        var maxX = poly.Max(p => p.X);
        var maxY = poly.Max(p => p.Y);
        return new SKRect(minX, minY, maxX, maxY);
    }

    private static bool Inside(SKPoint[] poly, float x, float y)
    {
        var inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            if (poly[i].Y > y != poly[j].Y > y &&
                x < (poly[j].X - poly[i].X) * (y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X)
                inside = !inside;
        }

        return inside;
    }

    private static SKRect Shrink(SKRect r, float d)
        => new(r.Left + d, r.Top + d, r.Right - d, r.Bottom - d);

    private static bool Contains(SKRect outer, SKRect inner)
        => inner.Left >= outer.Left - 0.5f && inner.Top >= outer.Top - 0.5f &&
           inner.Right <= outer.Right + 0.5f && inner.Bottom <= outer.Bottom + 0.5f;

    /// <summary>WCAG 2.x 相对亮度对比度。</summary>
    public static double Contrast(SKColor a, SKColor b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        var hi = Math.Max(la, lb);
        var lo = Math.Min(la, lb);
        return (hi + 0.05) / (lo + 0.05);
    }

    private static double Luminance(SKColor c)
    {
        static double Ch(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Ch(c.Red) + 0.7152 * Ch(c.Green) + 0.0722 * Ch(c.Blue);
    }
}
