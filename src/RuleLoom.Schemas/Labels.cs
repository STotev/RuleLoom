using System.Text;

namespace RuleLoom.Schemas;

/// <summary>
/// Readable labels from names: <c>customerTier</c> → "Customer tier", <c>OrderPlaced</c> → "Order placed",
/// <c>vatID</c> → "Vat ID".
/// </summary>
internal static class Labels
{
    public static string FromName(string name)
    {
        var words = SplitWords(name);
        if (words.Count == 0)
            return name;

        var label = new StringBuilder();
        for (var i = 0; i < words.Count; i++)
        {
            var word = words[i];
            var isAcronym = word.Length > 1 && word.All(char.IsUpper);

            if (i > 0)
                label.Append(' ');

            if (isAcronym)
                label.Append(word);
            else if (i == 0)
                label.Append(char.ToUpperInvariant(word[0])).Append(word[1..].ToLowerInvariant());
            else
                label.Append(word.ToLowerInvariant());
        }

        return label.ToString();
    }

    private static List<string> SplitWords(string name)
    {
        var words = new List<string>();
        var current = new StringBuilder();

        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];

            if (c is '_' or '-' or ' ' or '.')
            {
                Flush();
                continue;
            }

            var startsWord = current.Length > 0 && char.IsUpper(c) &&
                             (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1]) ||
                              (i + 1 < name.Length && char.IsLower(name[i + 1])));

            if (startsWord)
                Flush();

            current.Append(c);
        }

        Flush();
        return words;

        void Flush()
        {
            if (current.Length > 0)
                words.Add(current.ToString());
            current.Clear();
        }
    }
}
