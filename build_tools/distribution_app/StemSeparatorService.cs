using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DonOriginal3DMusic3;

internal sealed record StemFiles(
    string Vocals,
    string Drums,
    string Bass,
    string Other);

internal static class StemSeparatorService
{
    public static string ExecutablePath => Path.Combine(
        AppContext.BaseDirectory,
        "tools",
        "stem_separator",
        "stem_separator.exe");

    public static string CacheDirectory => Path.Combine(
        AppContext.BaseDirectory,
        "assets",
        "models",
        "demucs_onnx_cache");

    public static bool AssetsAvailable(out string missing)
    {
        if (!File.Exists(ExecutablePath))
        {
            missing = ExecutablePath;
            return false;
        }

        if (!Directory.Exists(CacheDirectory) ||
            !Directory.EnumerateFiles(
                CacheDirectory,
                "*.onnx",
                SearchOption.AllDirectories).Any())
        {
            missing = CacheDirectory;
            return false;
        }

        missing = "";
        return true;
    }

    public static async Task CreateSeparatorInputAsync(
        string ffmpeg,
        string input,
        string outputWav,
        double? startSeconds,
        double? durationSeconds,
        CancellationToken token)
    {
        var args = new List<string>
        {
            "-hide_banner", "-loglevel", "error", "-y"
        };

        if (startSeconds.HasValue)
        {
            args.Add("-ss");
            args.Add(startSeconds.Value.ToString(
                CultureInfo.InvariantCulture));
        }

        args.Add("-i");
        args.Add(input);

        if (durationSeconds.HasValue)
        {
            args.Add("-t");
            args.Add(durationSeconds.Value.ToString(
                CultureInfo.InvariantCulture));
        }

        args.AddRange(new[]
        {
            "-vn",
            "-ac", "2",
            "-ar", "44100",
            "-c:a", "pcm_f32le",
            outputWav
        });

        await FfmpegService.RunAsync(ffmpeg, args, token);
    }

    public static async Task<StemFiles> SeparateAsync(
        string inputWav,
        string outputDirectory,
        IProgress<int>? progress,
        Action<string>? log,
        CancellationToken token)
    {
        if (!AssetsAvailable(out string missing))
            throw new FileNotFoundException(
                "AI Stem分離エンジンまたはモデルが見つかりません。",
                missing);

        Directory.CreateDirectory(outputDirectory);

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = ExecutablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = AppContext.BaseDirectory,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (string arg in new[]
        {
            "separate",
            inputWav,
            outputDirectory,
            "--model", "htdemucs",
            "--precision", "fp16weights",
            "--providers", "cpu",
            "--cache-dir", CacheDirectory,
            "--verbose"
        })
            process.StartInfo.ArgumentList.Add(arg);

        process.StartInfo.Environment["PYTHONUTF8"] = "1";
        // 配布物に固定したSnapshotモデルだけを使用し、初回通信を禁止。
        process.StartInfo.Environment["HF_HUB_OFFLINE"] = "1";
        process.StartInfo.Environment["HF_HUB_DISABLE_TELEMETRY"] = "1";

        var errors = new List<string>();
        var gate = new object();

        process.Start();
        using var reg = token.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(true);
            }
            catch { }
        });

        async Task ReadStdoutAsync()
        {
            while (true)
            {
                string? line = await process.StandardOutput.ReadLineAsync(token);
                if (line is null) break;

                Match m = Regex.Match(
                    line,
                    @"chunk\s+(?<cur>\d+)\/(?<total>\d+)",
                    RegexOptions.IgnoreCase |
                    RegexOptions.CultureInvariant);

                if (m.Success &&
                    int.TryParse(m.Groups["cur"].Value, out int cur) &&
                    int.TryParse(m.Groups["total"].Value, out int total) &&
                    total > 0)
                {
                    progress?.Report(
                        Math.Clamp(
                            (int)Math.Round(cur * 100.0 / total),
                            0,
                            100));
                }
                else if (!string.IsNullOrWhiteSpace(line))
                {
                    log?.Invoke("[Stem] " + line.Trim());
                }
            }
        }

        async Task ReadStderrAsync()
        {
            while (true)
            {
                string? line = await process.StandardError.ReadLineAsync(token);
                if (line is null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;

                lock (gate)
                    errors.Add(line.Trim());
            }
        }

        await Task.WhenAll(
            ReadStdoutAsync(),
            ReadStderrAsync(),
            process.WaitForExitAsync(token));

        if (process.ExitCode != 0)
        {
            string message;
            lock (gate)
                message = string.Join(
                    Environment.NewLine,
                    errors.TakeLast(20));

            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(message)
                    ? $"AI Stem分離に失敗しました。終了コード: {process.ExitCode}"
                    : message);
        }

        string vocals = Path.Combine(outputDirectory, "vocals.wav");
        string drums = Path.Combine(outputDirectory, "drums.wav");
        string bass = Path.Combine(outputDirectory, "bass.wav");
        string other = Path.Combine(outputDirectory, "other.wav");

        foreach (string stem in new[] { vocals, drums, bass, other })
        {
            if (!File.Exists(stem) ||
                new FileInfo(stem).Length <= 44)
                throw new InvalidDataException(
                    "Stem分離結果が不完全です: " +
                    Path.GetFileName(stem));
        }

        progress?.Report(100);
        return new StemFiles(vocals, drums, bass, other);
    }
}
