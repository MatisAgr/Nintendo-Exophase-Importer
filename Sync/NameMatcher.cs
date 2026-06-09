using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SwitchPlaytimeExophase.Sync
{
    /// <summary>
    /// Normalizes game titles so an Exophase entry can be matched against a Playnite
    /// game even when punctuation, accents, trademark symbols or casing differ.
    /// </summary>
    public static class NameMatcher
    {
        private static readonly Regex NonAlphanumeric = new Regex(@"[^a-z0-9]+", RegexOptions.Compiled);
        private static readonly Regex LeadingArticle = new Regex(@"^(the|a|an)\s+", RegexOptions.Compiled);

        public static string Normalize(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return string.Empty;
            }

            var lowered = title.ToLowerInvariant();
            var withoutDiacritics = RemoveDiacritics(lowered);
            var spaced = NonAlphanumeric.Replace(withoutDiacritics, " ").Trim();
            spaced = LeadingArticle.Replace(spaced, string.Empty);
            // Collapse remaining whitespace.
            spaced = Regex.Replace(spaced, @"\s+", " ").Trim();
            return spaced;
        }

        private static string RemoveDiacritics(string text)
        {
            var normalized = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(normalized.Length);
            foreach (var c in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                {
                    sb.Append(c);
                }
            }
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }
    }
}
