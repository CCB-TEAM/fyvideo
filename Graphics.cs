namespace SkiaVideoLab;

/// <summary>一个可渲染的场景：给定时间画一帧。</summary>
public interface IScene
{
    (int Width, int Height) Size { get; }

    /// <param name="t">当前时间（秒）</param>
    /// <param name="frame">当前帧号</param>
    void Draw(SKCanvas canvas, double t, int frame, int width, int height);
}

/// <summary>缓动。片子里的所有入场/转场都走这几个，保证节奏统一。</summary>
internal static class Ease
{
    public static double OutCubic(double x)
    {
        x = Math.Clamp(x, 0, 1);
        return 1 - Math.Pow(1 - x, 3);
    }

    public static double OutQuint(double x)
    {
        x = Math.Clamp(x, 0, 1);
        return 1 - Math.Pow(1 - x, 5);
    }

    /// <summary>两端慢、中间快——转场用。</summary>
    public static double InOutCubic(double x)
    {
        x = Math.Clamp(x, 0, 1);
        return x < 0.5 ? 4 * x * x * x : 1 - Math.Pow(-2 * x + 2, 3) / 2;
    }

    /// <summary>0→1 的区间映射，自动 clamp。</summary>
    public static double Ramp(double t, double start, double end)
        => Math.Clamp((t - start) / Math.Max(1e-6, end - start), 0, 1);
}

/// <summary>字体探测：给 `fonts` 命令用，确认中文字形不是 .notdef。</summary>
internal static class Fonts
{
    private static readonly string[] Candidates =
    [
        "Microsoft YaHei UI", "Microsoft YaHei", "Noto Sans CJK SC", "Source Han Sans SC", "SimHei", "Segoe UI",
    ];

    public static SKTypeface Resolve(SKFontStyle style)
    {
        foreach (var name in Candidates)
        {
            var typeface = SKTypeface.FromFamilyName(name, style);
            if (typeface is not null && !typeface.FamilyName.Contains("DejaVu", StringComparison.OrdinalIgnoreCase))
                return typeface;
        }

        return SKTypeface.Default;
    }
}
