using System.Diagnostics;

namespace AudioBookBot.Services;

public class EdgeTtsService
{
    private readonly Dictionary<string, string> _voices = new()
    {
        ["uz"] = "uz-UZ-SardorNeural",
        ["tr"] = "tr-TR-EmelNeural",
        ["ru"] = "ru-RU-SvetlanaNeural",
        ["en"] = "en-US-JennyNeural",
    };

    private readonly string _ffmpeg = "ffmpeg";
    private const int MaxCharsPerRequest = 8000;
    private const int TimeoutSeconds = 60;

    public async Task<string> TextToOgg(string text, string language, CancellationToken ct = default)
    {
        if (!_voices.TryGetValue(language, out var voice))
            voice = _voices["en"];

        var oggPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.ogg");

        if (text.Length > MaxCharsPerRequest)
        {
            var chunks = SplitText(text, MaxCharsPerRequest);
            Console.WriteLine($"[EdgeTTS] {chunks.Count} ta chunk ketma-ket...");
            await ProcessChunksSequential(chunks, voice, oggPath, ct);
        }
        else
        {
            var mp3Path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.mp3");
            try
            {
                await RunEdgeTts(text, voice, mp3Path, ct);
                ct.ThrowIfCancellationRequested();
                await ConvertToOgg(mp3Path, oggPath, ct);
            }
            finally
            {
                if (File.Exists(mp3Path)) File.Delete(mp3Path);
            }
        }

        return oggPath;
    }

    // ✅ Parallel o'rniga ketma-ket — hang bo'lmaydi
    private async Task ProcessChunksSequential(List<string> chunks, string voice, string finalOgg, CancellationToken ct)
    {
        var mp3Files = new List<string>();

        try
        {
            for (int i = 0; i < chunks.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var mp3Path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}_chunk.mp3");
                mp3Files.Add(mp3Path);

                Console.WriteLine($"[EdgeTTS] Chunk {i + 1}/{chunks.Count}...");
                await RunEdgeTts(chunks[i], voice, mp3Path, ct);
                Console.WriteLine($"[EdgeTTS] Chunk {i + 1}/{chunks.Count} tayyor");
            }

            ct.ThrowIfCancellationRequested();

            var existing = mp3Files.Where(File.Exists).ToList();
            if (existing.Count == 0)
                throw new Exception("Hech qanday MP3 yaratilmadi!");
            else if (existing.Count == 1)
                await ConvertToOgg(existing[0], finalOgg, ct);
            else
                await MergeAndConvert(existing, finalOgg, ct);
        }
        finally
        {
            foreach (var f in mp3Files.Where(File.Exists))
                File.Delete(f);
        }
    }

    private async Task RunEdgeTts(string text, string voice, string outputPath, CancellationToken ct)
    {
        var textFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.txt");
        await File.WriteAllTextAsync(textFile, text, System.Text.Encoding.UTF8, ct);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "edge-tts",
                Arguments = $"--voice {voice} --file \"{textFile}\" --write-media \"{outputPath}\"",
                UseShellExecute = false,
                RedirectStandardError = false, // ✅ stderr o'qilmaydi — hang bo'lmaydi
                RedirectStandardOutput = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi)
                ?? throw new Exception("edge-tts ishga tushmadi. 'pip install edge-tts' qiling.");

            // ✅ Foydalanuvchi tokeni + timeout birlashtirildi
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            await using var reg = linked.Token.Register(() =>
            {
                try { process.Kill(entireProcessTree: true); } catch { }
            });

            try
            {
                await process.WaitForExitAsync(linked.Token);
            }
            catch (OperationCanceledException)
            {
                ct.ThrowIfCancellationRequested(); // /stop bosildi
                throw new Exception($"edge-tts {TimeoutSeconds}s timeout! Tarmoq muammosi bo'lishi mumkin.");
            }

            if (!File.Exists(outputPath))
                throw new Exception("edge-tts MP3 yaratmadi.");

            Console.WriteLine($"[EdgeTTS] OK: {Path.GetFileName(outputPath)}");
        }
        finally
        {
            if (File.Exists(textFile)) File.Delete(textFile);
        }
    }

    private async Task MergeAndConvert(List<string> mp3Files, string finalOgg, CancellationToken ct)
    {
        var mergedMp3 = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}_merged.mp3");
        var listFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}_list.txt");

        try
        {
            await File.WriteAllLinesAsync(listFile, mp3Files.Select(f => $"file '{f}'"), ct);

            var psi = new ProcessStartInfo
            {
                FileName = _ffmpeg,
                Arguments = $"-f concat -safe 0 -i \"{listFile}\" -c copy \"{mergedMp3}\" -y",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var p = Process.Start(psi)!;
            await using var reg = ct.Register(() => { try { p.Kill(entireProcessTree: true); } catch { } });
            await p.WaitForExitAsync(ct);

            ct.ThrowIfCancellationRequested();
            await ConvertToOgg(mergedMp3, finalOgg, ct);
        }
        finally
        {
            if (File.Exists(listFile)) File.Delete(listFile);
            if (File.Exists(mergedMp3)) File.Delete(mergedMp3);
        }
    }

    private async Task ConvertToOgg(string inputPath, string oggPath, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _ffmpeg,
            Arguments = $"-i \"{inputPath}\" -c:a libopus -b:a 64k \"{oggPath}\" -y",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi) ?? throw new Exception("FFmpeg ishga tushmadi");
        await using var reg = ct.Register(() => { try { process.Kill(entireProcessTree: true); } catch { } });
        await process.WaitForExitAsync(ct);
        ct.ThrowIfCancellationRequested();

        if (!File.Exists(oggPath))
            throw new Exception("FFmpeg OGG yaratmadi.");
    }

    private static List<string> SplitText(string text, int maxChars)
    {
        var chunks = new List<string>();
        var sentences = text.Split(new[] { ". ", "! ", "? ", "\n\n" }, StringSplitOptions.RemoveEmptyEntries);

        var current = new System.Text.StringBuilder();
        foreach (var sentence in sentences)
        {
            if (current.Length + sentence.Length > maxChars && current.Length > 0)
            {
                chunks.Add(current.ToString().Trim());
                current.Clear();
            }
            current.Append(sentence).Append(". ");
        }

        if (current.Length > 0)
            chunks.Add(current.ToString().Trim());

        return chunks.Count > 0 ? chunks : [text];
    }
}