namespace AudioBookBot.Services;

/// O'zbek kiril → lotin transliteratsiya
public static class UzbekTransliterator
{
    private static readonly Dictionary<string, string> Map = new()
    {
        // Ko'p harfli (avval tekshiriladi)
        {"шч", "shch"}, {"Шч", "Shch"},

        // Maxsus o'zbek harflari
        {"ў", "o'"},  {"Ў", "O'"},
        {"қ", "q"},   {"Қ", "Q"},
        {"ғ", "g'"},  {"Ғ", "G'"},
        {"ҳ", "h"},   {"Ҳ", "H"},

        // Oddiy kiril
        {"а", "a"},  {"А", "A"},
        {"б", "b"},  {"Б", "B"},
        {"в", "v"},  {"В", "V"},
        {"г", "g"},  {"Г", "G"},
        {"д", "d"},  {"Д", "D"},
        {"е", "e"},  {"Е", "E"},
        {"ё", "yo"}, {"Ё", "Yo"},
        {"ж", "j"},  {"Ж", "J"},
        {"з", "z"},  {"З", "Z"},
        {"и", "i"},  {"И", "I"},
        {"й", "y"},  {"Й", "Y"},
        {"к", "k"},  {"К", "K"},
        {"л", "l"},  {"Л", "L"},
        {"м", "m"},  {"М", "M"},
        {"н", "n"},  {"Н", "N"},
        {"о", "o"},  {"О", "O"},
        {"п", "p"},  {"П", "P"},
        {"р", "r"},  {"Р", "R"},
        {"с", "s"},  {"С", "S"},
        {"т", "t"},  {"Т", "T"},
        {"у", "u"},  {"У", "U"},
        {"ф", "f"},  {"Ф", "F"},
        {"х", "x"},  {"Х", "X"},
        {"ц", "ts"}, {"Ц", "Ts"},
        {"ч", "ch"}, {"Ч", "Ch"},
        {"ш", "sh"}, {"Ш", "Sh"},
        {"ъ", ""},   {"Ъ", ""},
        {"ы", "i"},  {"Ы", "I"},
        {"ь", ""},   {"Ь", ""},
        {"э", "e"},  {"Э", "E"},
        {"ю", "yu"}, {"Ю", "Yu"},
        {"я", "ya"}, {"Я", "Ya"},
        {"ни", "ni"},
    };

    public static string ToLatin(string text)
    {
        var result = new System.Text.StringBuilder(text.Length * 2);
        int i = 0;
        while (i < text.Length)
        {
            // 2 harfli kombinatsiyani tekshir
            if (i + 1 < text.Length)
            {
                var two = text.Substring(i, 2);
                if (Map.TryGetValue(two, out var twoResult))
                {
                    result.Append(twoResult);
                    i += 2;
                    continue;
                }
            }

            // 1 harfli
            var one = text[i].ToString();
            if (Map.TryGetValue(one, out var oneResult))
                result.Append(oneResult);
            else
                result.Append(text[i]); // o'zgarmagan (raqam, tinish belgi)

            i++;
        }
        return result.ToString();
    }

    /// O'zbek kiril harflari bormi?
    public static bool HasUzbekCyrillic(string text)
    {
        return text.Any(c => "ўқғҳЎҚҒҲ".Contains(c));
    }

    /// Oddiy kiril harflari bormi?
    public static bool HasCyrillic(string text)
    {
        return text.Any(c => c >= 'а' && c <= 'я' || c >= 'А' && c <= 'Я');
    }
}
