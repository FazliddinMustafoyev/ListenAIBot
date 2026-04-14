using System.Diagnostics;

namespace AudioBookBot.Services;

public class PiperService
{
    private readonly string _piperExe = @"C:\tts\piper\piper.exe";
    private readonly string _espeak = @"C:\tts\piper\espeak-ng-data";
    private readonly string _ffmpeg = "ffmpeg";

    private readonly EdgeTtsService _edgeTts = new();
    private readonly HashSet<string> _edgeTtsLanguages = ["uz", "uz_cyrillic", "tr"];

    private const int MaxParallel = 3;

    private readonly Dictionary<string, (string model, string config)> _models = new()
    {
        ["ru"] = (
            @"C:\tts\piper\ru_RU-irina-medium.onnx",
            @"C:\tts\piper\ru_RU-irina-medium.onnx.json"
        ),
        ["en"] = (
            @"C:\tts\piper\en_US-lessac-medium.onnx",
            @"C:\tts\piper\en_US-lessac-medium.onnx.json"
        ),
    };

    // ✅ CancellationToken qo'shildi
    public async Task<string> TextToOgg(string text, string language, CancellationToken ct = default)
    {
        if (language == "uz_cyrillic")
        {
            Console.WriteLine("[TTS] O'zbek kiril → latin");
            text = UzbekTransliterator.ToLatin(text);
            language = "uz";
        }

        if (_edgeTtsLanguages.Contains(language))
        {
            try
            {
                Console.WriteLine($"[TTS] {language} → Edge TTS");
                return await _edgeTts.TextToOgg(text, language, ct);
            }
            catch (OperationCanceledException)
            {
                throw; // to'xtatildi — yuqoriga uzat
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TTS] Edge TTS ishlamadi: {ex.Message}, rus modeliga o'tilmoqda...");
                language = "ru";
            }
        }

        var modelInfo = GetAvailableModel(language);
        Console.WriteLine($"[TTS] {language} → Piper: {Path.GetFileName(modelInfo.model)}");

        var wavPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.wav");
        var oggPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.ogg");

        try
        {
            await RunPiperParallel(text, modelInfo.model, modelInfo.config, wavPath, ct);
            ct.ThrowIfCancellationRequested();
            await ConvertToOgg(wavPath, oggPath, ct);
            return oggPath;
        }
        finally
        {
            if (File.Exists(wavPath)) File.Delete(wavPath);
        }
    }

    private (string model, string config) GetAvailableModel(string language)
    {
        if (_models.TryGetValue(language, out var m) && File.Exists(m.model)) return m;
        if (File.Exists(_models["ru"].model)) return _models["ru"];
        if (File.Exists(_models["en"].model)) return _models["en"];
        throw new FileNotFoundException("Hech qanday Piper modeli topilmadi!");
    }

    private async Task RunPiperParallel(string text, string model, string config, string outputWav, CancellationToken ct)
    {
        var chunks = SplitText(text, maxChars: 3000);
        Console.WriteLine($"[Piper] {chunks.Count} ta chunk, parallel={Math.Min(MaxParallel, chunks.Count)}");

        var tempWavs = chunks.Select(_ =>
            Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}_chunk.wav")
        ).ToList();

        var semaphore = new SemaphoreSlim(MaxParallel);
        var tasks = chunks.Select(async (chunk, i) =>
        {
            await semaphore.WaitAsync(ct); // ✅ cancel bo'lsa kutmaydi
            try
            {
                ct.ThrowIfCancellationRequested();
                var sw = Stopwatch.StartNew();
                await RunSinglePiper(chunk, model, config, tempWavs[i], ct);
                Console.WriteLine($"[Piper] Chunk {i + 1}/{chunks.Count} tayyor ({sw.ElapsedMilliseconds}ms)");
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        var existingWavs = tempWavs.Where(File.Exists).ToList();
        try
        {
            if (existingWavs.Count == 0)
                throw new Exception("Hech qanday WAV yaratilmadi!");
            else if (existingWavs.Count == 1)
                File.Copy(existingWavs[0], outputWav, overwrite: true);
            else
                await MergeWavFiles(tempWavs.Where(File.Exists).ToList(), outputWav, ct);
        }
        finally
        {
            foreach (var tmp in tempWavs.Where(File.Exists))
                File.Delete(tmp);
        }
    }

    // ✅ Process.Kill() — cancel bo'lsa darhol o'ldiriladi
    private async Task RunSinglePiper(string text, string model, string config, string chunkWav, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _piperExe,
            Arguments = $"--model \"{model}\" --config \"{config}\" --espeak_data \"{_espeak}\" --output_file \"{chunkWav}\"",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi) ?? throw new Exception("Piper ishga tushmadi");

        // ✅ Cancel bo'lsa processni o'ldir
        await using var reg = ct.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); }
            catch { /* allaqachon tugagan bo'lishi mumkin */ }
        });

        await process.StandardInput.WriteLineAsync(text);
        process.StandardInput.Close();
        await process.WaitForExitAsync(ct);

        ct.ThrowIfCancellationRequested();

        if (!File.Exists(chunkWav))
            throw new Exception($"Piper WAV yaratmadi. Matn: {text[..Math.Min(50, text.Length)]}...");
    }

    private async Task MergeWavFiles(List<string> wavFiles, string outputPath, CancellationToken ct)
    {
        var listFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}_list.txt");
        await File.WriteAllLinesAsync(listFile, wavFiles.Select(f => $"file '{f}'"), ct);

        var psi = new ProcessStartInfo
        {
            FileName = _ffmpeg,
            Arguments = $"-f concat -safe 0 -i \"{listFile}\" -c copy \"{outputPath}\" -y",
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)!;

        // ✅ Cancel bo'lsa ffmpeg ham o'ldiriladi
        await using var reg = ct.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); }
            catch { }
        });

        await process.WaitForExitAsync(ct);
        File.Delete(listFile);
    }

    private async Task ConvertToOgg(string wavPath, string oggPath, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _ffmpeg,
            Arguments = $"-i \"{wavPath}\" -c:a libopus -b:a 64k \"{oggPath}\" -y",
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi) ?? throw new Exception("FFmpeg ishga tushmadi");

        // ✅ Cancel bo'lsa ffmpeg o'ldiriladi
        await using var reg = ct.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); }
            catch { }
        });

        await process.WaitForExitAsync(ct);
        ct.ThrowIfCancellationRequested();

        if (!File.Exists(oggPath))
            throw new Exception("FFmpeg OGG yaratmadi.");
    }

    private static List<string> SplitText(string text, int maxChars = 3000)
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