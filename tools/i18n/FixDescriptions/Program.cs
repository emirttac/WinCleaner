using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;

var root = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
var i18n = Path.Combine(root, "tools", "i18n");
var langs = new[] { "en", "tr", "de", "es", "zh", "fr", "ro", "ru" };

var optsOut = new JsonSerializerOptions
{
    WriteIndented = true,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
};

foreach (var lang in langs)
{
    var path = Path.Combine(i18n, $"{lang}.json");
    using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
    var map = new Dictionary<string, string>();
    var fixedCount = 0;

    foreach (var prop in doc.RootElement.EnumerateObject())
    {
        var v = prop.Value.GetString() ?? "";
        var n = Normalize(v);
        if (n != v) fixedCount++;
        map[prop.Name] = n;
    }

    // Preserve original key order
    var ordered = new Dictionary<string, string>();
    foreach (var prop in doc.RootElement.EnumerateObject())
        ordered[prop.Name] = map[prop.Name];

    File.WriteAllText(path, JsonSerializer.Serialize(ordered, optsOut) + Environment.NewLine, new UTF8Encoding(false));
    Console.WriteLine($"{lang}: fixed {fixedCount} strings, keys={ordered.Count}");
}

static string Normalize(string v)
{
    // Broken newline encodings that render as literal "&#x0a;" in UI
    v = v.Replace("&#x0a;", "\n", StringComparison.OrdinalIgnoreCase);
    v = v.Replace("&amp;#x0a;", "\n", StringComparison.OrdinalIgnoreCase);
    v = v.Replace("&#10;", "\n", StringComparison.OrdinalIgnoreCase);
    v = v.Replace("&amp;#10;", "\n", StringComparison.OrdinalIgnoreCase);

    // Collapse Windows newlines
    v = v.Replace("\r\n", "\n").Replace("\r", "\n");

    // Guillemets used as quotes -> normal quotes (cleaner in UI)
    v = v.Replace('«', '"').Replace('»', '"');

    // Common double-escaped amp leftovers stored as literal text
    while (v.Contains("&amp;amp;", StringComparison.Ordinal))
        v = v.Replace("&amp;amp;", "&amp;", StringComparison.Ordinal);

    return v.Trim();
}
