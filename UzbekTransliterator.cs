using System.Text;

namespace AudioBookBot.Services;

public static class UzbekTransliterator
{
    private static readonly Dictionary<char, string> Map = new()
    {
        // Kichik harflar
        {'а', "a"}, {'б', "b"}, {'в', "v"}, {'г', "g"}, {'д', "d"},
        {'е', "e"}, {'ё', "yo"}, {'ж', "j"}, {'з', "z"}, {'и', "i"},
        {'й', "y"}, {'к', "k"}, {'л', "l"}, {'м', "m"}, {'н', "n"},
        {'о', "o"}, {'п', "p"}, {'р', "r"}, {'с', "s"}, {'т', "t"},
        {'у', "u"}, {'ф', "f"}, {'х', "x"}, {'ц', "ts"}, {'ч', "ch"},
        {'ш', "sh"}, {'щ', "sh"}, {'ъ', ""}, {'ы', "i"}, {'ь', ""},
        {'э', "e"}, {'ю', "yu"}, {'я', "ya"},
        
        // O‘zbek maxsus harflari (APOSTROF EMAS, balki ‘ belgisi)
        {'ў', "o‘"}, {'қ', "q"}, {'ғ', "g‘"}, {'ҳ', "h"},
        
        // Katta harflar
        {'А', "A"}, {'Б', "B"}, {'В', "V"}, {'Г', "G"}, {'Д', "D"},
        {'Е', "E"}, {'Ё', "Yo"}, {'Ж', "J"}, {'З', "Z"}, {'И', "I"},
        {'Й', "Y"}, {'К', "K"}, {'Л', "L"}, {'М', "M"}, {'Н', "N"},
        {'О', "O"}, {'П', "P"}, {'Р', "R"}, {'С', "S"}, {'Т', "T"},
        {'У', "U"}, {'Ф', "F"}, {'Х', "X"}, {'Ц', "Ts"}, {'Ч', "Ch"},
        {'Ш', "Sh"}, {'Щ', "Sh"}, {'Ъ', ""}, {'Ы', "I"}, {'Ь', ""},
        {'Э', "E"}, {'Ю', "Yu"}, {'Я', "Ya"},
        {'Ў', "O‘"}, {'Қ', "Q"}, {'Ғ', "G‘"}, {'Ҳ', "H"}
    };

    public static string ToLatin(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var result = new StringBuilder(text.Length * 2);

        foreach (char c in text)
        {
            if (Map.TryGetValue(c, out string? latin))
                result.Append(latin);
            else
                result.Append(c); // tinish belgilari, raqamlar, bo'shliqlar
        }

        return result.ToString();
    }

    public static bool HasUzbekCyrillic(string text)
    {
        return text.Any(c => "ўқғҳЎҚҒҲ".Contains(c));
    }

    public static bool HasCyrillic(string text)
    {
        return text.Any(c => c >= 'а' && c <= 'я' || c >= 'А' && c <= 'Я' || c == 'ё' || c == 'Ё');
    }
}