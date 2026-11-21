using System.Diagnostics;

namespace SkiaVideoLab;

public sealed record FfmpegPipeSettings(
    string FfmpegPath,
    int Width,
    int Height,
    int Fps,
    string Output,
    int Crf = 18,
    string Preset = "medium",
    string? AudioPath = null);

/// <summary>
/// 把原始 BGRA 帧写进 ffmpeg 的 stdin。
/// 比"先导 PNG 序列再编码"省掉一次落盘 + 一次解码，1080p 下通常快 2~4 倍。
/// </summary>
public sealed class FfmpegPipe : IDisposable
{
    private readonly Process _process;
    private readonly Stream _stdin;
    private readonly Task<string> _stderr;
    private bool _finished;

    private FfmpegPipe(Process process, Task<string> stderr)
    {
        _process = process;
        _stdin = process.StandardInput.BaseStream;
        _stderr = stderr;
    }

    public static FfmpegPipe Start(FfmpegPipeSettings s)
    {
        var args = new List<string>
        {
            "-hide_banner", "-loglevel", "error", "-y",
            // 原始帧输入
            "-f", "rawvideo",
            "-pixel_format", "bgra",
            "-video_size", $"{s.Width}x{s.Height}",
            "-framerate", s.Fps.ToString(),
            "-i", "pipe:0",
        };

        if (!string.IsNullOrWhiteSpace(s.AudioPath))
        {
            args.AddRange(["-i", s.AudioPath!]);
            args.AddRange(["-c:a", "aac", "-b:a", "192k", "-shortest"]);
        }

        args.AddRange([
            "-c:v", "libx264",
            "-preset", s.Preset,
            "-crf", s.Crf.ToString(),
            // yuv420p 是唯一能在所有播放器 / 手机 / 微信里正常播的像素格式
            "-pix_fmt", "yuv420p",
            "-movflags", "+faststart",
            s.Output,
        ]);

        var psi = new ProcessStartInfo(s.FfmpegPath)
        {
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        var process = Process.Start(psi) ?? throw new InvalidOperationException("无法启动 ffmpeg");

        // 必须持续排空 stderr，否则管道写满后 ffmpeg 会阻塞、渲染卡死。
        var stderr = Task.Run(() => process.StandardError.ReadToEnd());
        _ = process.StandardOutput.ReadToEndAsync();

        return new FfmpegPipe(process, stderr);
    }

    public void WriteFrame(byte[] frame)
    {
        try
        {
            _stdin.Write(frame, 0, frame.Length);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException($"写入 ffmpeg 失败（进程可能已退出）：{ex.Message}\n{_stderr.GetAwaiter().GetResult()}", ex);
        }
    }

    public void Finish()
    {
        if (_finished) return;
        _finished = true;

        _stdin.Flush();
        _stdin.Close();
        _process.WaitForExit();

        var errors = _stderr.GetAwaiter().GetResult();
        if (_process.ExitCode != 0)
            throw new InvalidOperationException($"ffmpeg 退出码 {_process.ExitCode}：\n{errors}");
    }

    public void Dispose()
    {
        if (!_finished)
        {
            try { _stdin.Close(); } catch { /* 已在关闭 */ }
            try { _process.Kill(entireProcessTree: true); } catch { /* 已退出 */ }
        }
        _process.Dispose();
    }
}
