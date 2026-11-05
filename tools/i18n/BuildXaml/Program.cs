using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

var root = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
var i18n = Path.Combine(root, "tools", "i18n");
var outDir = Path.Combine(root, "src", "WinCleaner", "Resources");
var langs = new[] { "en", "tr", "de", "es", "zh", "fr", "ro", "ru" };

static string Esc(string s)
{
    // Escape XML specials first, then encode newlines as character references
    // so WPF gets real line breaks (not the literal text "&#x0a;").
    s = s.Replace("&", "&amp;")
         .Replace("<", "&lt;")
         .Replace(">", "&gt;")
         .Replace("\"", "&quot;");
    s = s.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "&#x0a;");
    return s;
}

var enPath = Path.Combine(i18n, "en.json");
using var enDoc = JsonDocument.Parse(File.ReadAllText(enPath, Encoding.UTF8));
var enKeys = enDoc.RootElement.EnumerateObject().Select(p => p.Name).ToList();
Console.WriteLine($"EN keys: {enKeys.Count}");

foreach (var lang in langs)
{
    var path = Path.Combine(i18n, $"{lang}.json");
    using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
    var map = doc.RootElement.EnumerateObject()
        .ToDictionary(p => p.Name, p => p.Value.GetString() ?? "", StringComparer.Ordinal);

    var missing = enKeys.Where(k => !map.ContainsKey(k)).ToList();
    if (missing.Count > 0)
        throw new InvalidOperationException($"{lang} missing {missing.Count}: {missing[0]}");

    var sb = new StringBuilder();
    sb.AppendLine("<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"");
    sb.AppendLine("                    xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"");
    sb.AppendLine("                    xmlns:sys=\"clr-namespace:System;assembly=System.Runtime\">");
    sb.AppendLine();
    foreach (var k in enKeys)
    {
        var v = map[k];
        if (string.IsNullOrWhiteSpace(v))
            throw new InvalidOperationException($"{lang} empty {k}");
        sb.Append("  <sys:String x:Key=\"").Append(k).Append("\">")
          .Append(Esc(v)).AppendLine("</sys:String>");
    }
    sb.AppendLine();
    sb.AppendLine("</ResourceDictionary>");

    var outPath = Path.Combine(outDir, $"Strings.{lang}.xaml");
    File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    Console.WriteLine($"Wrote Strings.{lang}.xaml");
}

foreach (var lang in new[] { "zh", "ru", "de" })
{
    var text = File.ReadAllText(Path.Combine(outDir, $"Strings.{lang}.xaml"), Encoding.UTF8);
    var m = Regex.Match(text, "x:Key=\"nav.settings\">([^<]+)<");
    var codes = string.Join(",", m.Groups[1].Value.Select(c => ((int)c).ToString()));
    Console.WriteLine($"{lang} nav.settings codes={codes}");
}
