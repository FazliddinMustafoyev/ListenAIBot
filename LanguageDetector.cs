namespace AudioBookBot.Services;

public static class LanguageDetector
{
    // O'zbek lotin kalit so'zlari
    private static readonly HashSet<string> UzbekLatinKeywords =
    [
        "va", "bu", "bilan", "uchun", "ham", "lekin", "ammo", "yoki",
        "men", "sen", "biz", "siz", "ular", "shu", "edi",
        "assalomu", "alaykum", "rahmat", "xayr", "kerak", "mumkin",
        "kitob", "fayl", "tizim", "uning", "ning", "dan", "ga", "da",
        "bir", "ikki", "uch", "dedi", "deb", "emas", "hech", "nima",
        "qila", "qildi", "boʻldi", "boldi", "shunday", "chunki"
    ];

    // ✅ O'zbek KIRIL kalit so'zlari — maxsus harflarsiz yozilgan kitoblar uchun
    private static readonly HashSet<string> UzbekCyrillicKeywords =
    [
        "ва", "бу", "билан", "учун", "хам", "лекин", "аммо", "ёки",
        "мен", "сен", "биз", "сиз", "улар", "шу", "эди", "уни",
        "бир", "деди", "деб", "эмас", "хеч", "нима", "шундай",
        "чунки", "унинг", "нинг", "дан", "учун", "эса", "аммо",
        "бўлди", "булди", "килди", "қилди", "борди", "келди",
        "одам", "йил", "кун", "уй", "юрт", "халқ", "халк",
        "китоб", "ота", "она", "бола", "қалб", "калб"
    ];

    public static string Detect(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "en";
        var sample = text.Length > 1000 ? text[..1000] : text;
        var totalLetters = sample.Count(char.IsLetter);
        if (totalLetters == 0) return "en";

        // 1. O'zbek kiril maxsus harflari (ў қ ғ ҳ) → uz_cyrillic
        if (UzbekTransliterator.HasUzbekCyrillic(sample))
            return "uz_cyrillic";

        // 2. Kiril harflar bor — rus yoki o'zbek kiril?
        var cyrillicCount = sample.Count(c =>
            (c >= 'а' && c <= 'я') || (c >= 'А' && c <= 'Я') || c == 'ё' || c == 'Ё');

        if ((double)cyrillicCount / totalLetters > 0.4)
        {
            // ✅ O'zbek kiril kalit so'zlarini tekshir
            var lowerSample = sample.ToLower();
            var cyrillicWords = lowerSample
                .Split(new[] { ' ', '\n', '\r', '.', ',', '!', '?', ':', ';', '-', '«', '»', '"', '"' },
                    StringSplitOptions.RemoveEmptyEntries);

            var uzCyrCount = cyrillicWords.Count(w => UzbekCyrillicKeywords.Contains(w));
            var totalWords = cyrillicWords.Length;

            // 3+ o'zbek so'z yoki >8% so'z o'zbek bo'lsa → uz_cyrillic
            if (uzCyrCount >= 3 || (totalWords > 0 && (double)uzCyrCount / totalWords > 0.08))
                return "uz_cyrillic";

            return "ru";
        }

        // 3. O'zbek lotin so'zlari
        var latinWords = sample.ToLower()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var uzLatinCount = latinWords.Count(w => UzbekLatinKeywords.Contains(w));
        if (uzLatinCount >= 2) return "uz";

        return "en";
    }
}