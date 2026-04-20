using System.Collections.Concurrent;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using AudioBookBot.Services;

var botToken = "8626398460:AAFaHQAWTofFTL9RbqEcN28HwNZNbHKOwCc";
var bot = new TelegramBotClient(botToken);
var piperService = new PiperService();
var userTokens = new ConcurrentDictionary<long, CancellationTokenSource>();

using var cts = new CancellationTokenSource();

bot.StartReceiving(
    updateHandler: HandleUpdateAsync,
    errorHandler: HandleErrorAsync,
    receiverOptions: new ReceiverOptions { AllowedUpdates = [UpdateType.Message] },
    cancellationToken: cts.Token
);

var me = await bot.GetMe();
Console.WriteLine($"Bot started: @{me.Username}");
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
await Task.Delay(Timeout.Infinite, cts.Token).ContinueWith(_ => { });

CancellationTokenSource StartUserTask(long chatId)
{
    if (userTokens.TryGetValue(chatId, out var existing))
        existing.Cancel();
    var newCts = new CancellationTokenSource();
    userTokens[chatId] = newCts;
    return newCts;
}

void FinishUserTask(long chatId, CancellationTokenSource myCts)
{
    userTokens.TryRemove(new KeyValuePair<long, CancellationTokenSource>(chatId, myCts));
    myCts.Dispose();
}

// ✅ HandleUpdateAsync TEZDA qaytadi — og'ir ish background da
async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken ct)
{
    if (update.Message is not { } message) return;
    var chatId = message.Chat.Id;

    if (message.Text?.StartsWith("/start") == true)
    {
        await botClient.SendMessage(chatId,
            "📚 *AudioBook Bot*\n\n" +
            "Fayl yuboring, audio formatga aylantirib beraman!\n\n" +
            "📄 PDF — har 50 sahifadan 1 audio\n" +
            "📝 DOCX — to'liq audio\n" +
            "💬 Matn — to'g'ridan audio\n\n" +
            "Tillar: 🇷🇺 Rus | 🇺🇿 O'zbek | 🇬🇧 Ingliz",
            parseMode: ParseMode.Markdown,
            cancellationToken: ct);
        return;
    }

    // ✅ /stop — background task hali ishlayotgan bo'lsa cancel qiladi
    if (message.Text?.StartsWith("/stop") == true)
    {
        if (userTokens.TryGetValue(chatId, out var userCts))
        {
            userCts.Cancel();
            await botClient.SendMessage(chatId,
                "⛔ Jarayon to'xtatildi.\n\nYangi kitob yoki matn yuboring — tayyor!",
                cancellationToken: ct);
        }
        else
        {
            await botClient.SendMessage(chatId,
                "ℹ️ Hozir faol jarayon yo'q.",
                cancellationToken: ct);
        }
        return;
    }

    // ✅ Fayl — background Task da ishlaydi, handler DARHOL qaytadi
    if (message.Document is { } document)
    {
        var mimeType = document.MimeType ?? "";
        var fileName = document.FileName ?? "";

        bool isPdf = mimeType == "application/pdf"
                      || fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
        bool isDocx = mimeType == "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
                      || fileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase);

        if (!isPdf && !isDocx)
        {
            await botClient.SendMessage(chatId, "❌ Faqat PDF va DOCX fayllar qabul qilinadi.", cancellationToken: ct);
            return;
        }

        await botClient.SendMessage(chatId,
            $"📥 *{fileName}* qabul qilindi...\n\n/stop — jarayonni to'xtatish",
            parseMode: ParseMode.Markdown,
            cancellationToken: ct);

        var userCts = StartUserTask(chatId);

        // ✅ Fire-and-forget: handler qaytadi, /stop ishlashi mumkin bo'ladi
        _ = Task.Run(async () =>
        {
            var ext = isPdf ? ".pdf" : ".docx";
            var localPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}{ext}");
            try
            {
                var fileInfo = await botClient.GetFile(document.FileId);
                await using (var fs = System.IO.File.OpenWrite(localPath))
                    await botClient.DownloadFile(fileInfo.FilePath!, fs);

                if (isPdf)
                    await HandlePdf(botClient, chatId, localPath, fileName, userCts.Token);
                else
                    await HandleDocx(botClient, chatId, localPath, fileName, userCts.Token);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"[INFO] chatId={chatId} — to'xtatildi.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] {ex}");
                try
                {
                    await botClient.SendMessage(chatId,
                        $"❌ Xatolik: `{ex.Message}`",
                        parseMode: ParseMode.Markdown);
                }
                catch { }
            }
            finally
            {
                if (System.IO.File.Exists(localPath)) System.IO.File.Delete(localPath);
                FinishUserTask(chatId, userCts);
            }
        });

        return; // ✅ Darhol qaytadi
    }

    // ✅ Matn TTS ham background da
    if (!string.IsNullOrWhiteSpace(message.Text) && !message.Text.StartsWith("/"))
    {
        var userCts = StartUserTask(chatId);

        await botClient.SendMessage(chatId,
            "⏳ Audio tayyorlanmoqda...\n\n/stop — to'xtatish",
            cancellationToken: ct);

        _ = Task.Run(async () =>
        {
            try
            {
                var language = LanguageDetector.Detect(message.Text);
                var oggPath = await piperService.TextToOgg(message.Text, language, userCts.Token);

                userCts.Token.ThrowIfCancellationRequested();

                await using var audioStream = System.IO.File.OpenRead(oggPath);
                await botClient.SendVoice(chatId: chatId,
                    voice: InputFile.FromStream(audioStream, "tts.ogg"));

                System.IO.File.Delete(oggPath);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"[INFO] chatId={chatId} — matn TTS to'xtatildi.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] {ex}");
                try
                {
                    await botClient.SendMessage(chatId,
                        $"❌ Xatolik: `{ex.Message}`",
                        parseMode: ParseMode.Markdown);
                }
                catch { }
            }
            finally
            {
                FinishUserTask(chatId, userCts);
            }
        });
    }
}

async Task HandlePdf(ITelegramBotClient botClient, long chatId, string pdfPath, string fileName, CancellationToken ct)
{
    var totalPages = PdfService.GetPageCount(pdfPath);
    var chunks = PdfService.ExtractChunks(pdfPath);

    if (chunks.Count == 0)
    {
        await botClient.SendMessage(chatId,
            "❌ PDF dan matn ajratib bo'lmadi (scan qilingan bo'lishi mumkin).",
            cancellationToken: ct);
        return;
    }

    var language = LanguageDetector.Detect(chunks[0].text);
    var langEmoji = language switch
    {
        "ru" => "🇷🇺 Русский",
        "uz" or "uz_cyrillic" => "🇺🇿 O'zbek",
        "tr" => "🇹🇷 Türkçe",
        _ => "🇬🇧 English"
    };

    await botClient.SendMessage(chatId,
        $"📖 *{fileName}*\n" +
        $"📄 Jami sahifalar: {totalPages}\n" +
        $"🎵 Audio qismlar: {chunks.Count} ta (har 50 sahifa)\n" +
        $"🌐 Til: {langEmoji}\n\n" +
        $"⏳ Ketma-ket yuborilmoqda...",
        parseMode: ParseMode.Markdown,
        cancellationToken: ct);

    foreach (var (index, from, to, text) in chunks)
    {
        ct.ThrowIfCancellationRequested();

        await botClient.SendMessage(chatId,
            $"🔄 Qism {index}/{chunks.Count} tayyorlanmoqda (sahifa {from}-{to})...",
            cancellationToken: ct);

        var oggPath = await piperService.TextToOgg(text, language, ct);

        ct.ThrowIfCancellationRequested();

        await using var audioStream = System.IO.File.OpenRead(oggPath);
        await botClient.SendVoice(
            chatId: chatId,
            voice: InputFile.FromStream(audioStream, $"part_{index}.ogg"),
            caption: $"🎧 {fileName} | Qism {index}/{chunks.Count} | Sahifa {from}-{to}",
            cancellationToken: ct);

        System.IO.File.Delete(oggPath);
        await Task.Delay(300, ct);
    }

    await botClient.SendMessage(chatId,
        $"✅ *{fileName}* to'liq audio qilindi!\n🎧 Jami {chunks.Count} ta audio yuborildi.",
        parseMode: ParseMode.Markdown,
        cancellationToken: ct);
}

async Task HandleDocx(ITelegramBotClient botClient, long chatId, string docxPath, string fileName, CancellationToken ct)
{
    await botClient.SendMessage(chatId, "📝 Matn ajratilmoqda...", cancellationToken: ct);

    var text = DocxService.ExtractText(docxPath);
    if (string.IsNullOrWhiteSpace(text))
    {
        await botClient.SendMessage(chatId, "❌ DOCX fayl bo'sh yoki o'qib bo'lmadi.", cancellationToken: ct);
        return;
    }

    ct.ThrowIfCancellationRequested();

    var language = LanguageDetector.Detect(text);
    var langEmoji = language switch
    {
        "ru" => "🇷🇺 Русский",
        "uz" or "uz_cyrillic" => "🇺🇿 O'zbek",
        "tr" => "🇹🇷 Türkçe",
        _ => "🇬🇧 English"
    };

    // ✅ Matnni bo'laklarga ajrat (har biri ~5000 so'z)
    var chunks = SplitTextIntoChunks(text, maxWords: 5000);
    var wordCount = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

    await botClient.SendMessage(chatId,
        $"🌐 Til: {langEmoji}\n" +
        $"📊 So'zlar: {wordCount:N0}\n" +
        $"🎵 Audio qismlar: {chunks.Count} ta\n\n" +
        $"⏳ Ketma-ket yuborilmoqda...",
        cancellationToken: ct);

    for (int i = 0; i < chunks.Count; i++)
    {
        ct.ThrowIfCancellationRequested();

        await botClient.SendMessage(chatId,
            $"🔄 Qism {i + 1}/{chunks.Count} tayyorlanmoqda...",
            cancellationToken: ct);

        var oggPath = await piperService.TextToOgg(chunks[i], language, ct);

        ct.ThrowIfCancellationRequested();

        await using var audioStream = System.IO.File.OpenRead(oggPath);
        await botClient.SendVoice(
            chatId: chatId,
            voice: InputFile.FromStream(audioStream, $"part_{i + 1}.ogg"),
            caption: $"🎧 {fileName} | Qism {i + 1}/{chunks.Count}",
            cancellationToken: ct);

        System.IO.File.Delete(oggPath);
        await Task.Delay(300, ct);
    }

    await botClient.SendMessage(chatId,
        $"✅ *{fileName}* to'liq audio qilindi!\n🎧 Jami {chunks.Count} ta audio yuborildi.",
        parseMode: ParseMode.Markdown,
        cancellationToken: ct);
}

// ✅ Yangi helper — so'z soni bo'yicha bo'lish, gap o'rtasida kesmasligi uchun
static List<string> SplitTextIntoChunks(string text, int maxWords = 5000)
{
    var chunks = new List<string>();
    // Gaplarni to'liq saqlash uchun gap chegarasida kesadi
    var sentences = text.Split(
        new[] { ". ", "! ", "? ", ".\n", "!\n", "?\n" },
        StringSplitOptions.RemoveEmptyEntries);

    var current = new System.Text.StringBuilder();
    int currentWords = 0;

    foreach (var sentence in sentences)
    {
        var sentenceWords = sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

        if (currentWords + sentenceWords > maxWords && current.Length > 0)
        {
            chunks.Add(current.ToString().Trim());
            current.Clear();
            currentWords = 0;
        }

        current.Append(sentence).Append(". ");
        currentWords += sentenceWords;
    }

    if (current.Length > 0)
        chunks.Add(current.ToString().Trim());

    return chunks.Count > 0 ? chunks : [text];
}
Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken ct)
{
    Console.WriteLine(exception switch
    {
        ApiRequestException apiEx => $"Telegram API [{apiEx.ErrorCode}]: {apiEx.Message}",
        _ => exception.ToString()
    });
    return Task.CompletedTask;
}