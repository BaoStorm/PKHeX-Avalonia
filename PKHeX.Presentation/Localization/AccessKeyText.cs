using System.Text;

namespace PKHeX.Presentation.Localization;

/// <summary>
/// Turns a menu header that carries an access-key marker into plain text. Menu strings mark the
/// access key with an underscore (en <c>"_Open..."</c>, de <c>"Ö_ffnen..."</c>); a doubled
/// underscore is a literal underscore. Text shown outside a menu (lists, messages) must not show
/// the marker.
/// </summary>
public static class AccessKeyText
{
    /// <summary>Removes single <c>_</c> markers and turns <c>__</c> into <c>_</c>.</summary>
    public static string Strip(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        if (!text.Contains('_'))
            return text;

        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c != '_')
            {
                builder.Append(c);
                continue;
            }

            if (i + 1 < text.Length && text[i + 1] == '_')
            {
                builder.Append('_');
                i++;
            }
        }
        return builder.ToString();
    }
}
