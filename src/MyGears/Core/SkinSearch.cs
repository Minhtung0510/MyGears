using System.Globalization;
using System.Text;
namespace MyGears.Core;

public static class SkinSearch
{
    private static string Normalize(string value)
    {
        var text = new string(value.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        return text.ToLowerInvariant().Replace('đ', 'd');
    }
    public static List<string> Matches(ValorantScanResult? scan, string query)
    {
        var terms = Normalize(query.Trim()).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (scan == null || terms.Length == 0) return [];
        return scan.OwnedSkins.Where(s => terms.All(t => Normalize(s.DisplayName + " " + s.WeaponName).Contains(t)))
            .Select(s => s.DisplayName).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s).ToList();
    }
}
