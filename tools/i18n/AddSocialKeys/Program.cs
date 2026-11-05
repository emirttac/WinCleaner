using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;

var root = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
var i18n = Path.Combine(root, "tools", "i18n");

var additions = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
{
    ["en"] = new()
    {
        ["set.social.title"] = "Social",
        ["set.social.subtitle"] = "Follow emirttac on social media",
        ["set.social.youtube"] = "YouTube",
        ["set.social.instagram"] = "Instagram",
        ["set.social.github"] = "GitHub",
    },
    ["tr"] = new()
    {
        ["set.social.title"] = "Sosyal medya",
        ["set.social.subtitle"] = "emirttac sosyal medya hesapları",
        ["set.social.youtube"] = "YouTube",
        ["set.social.instagram"] = "Instagram",
        ["set.social.github"] = "GitHub",
    },
    ["de"] = new()
    {
        ["set.social.title"] = "Social Media",
        ["set.social.subtitle"] = "Folge emirttac in den sozialen Medien",
        ["set.social.youtube"] = "YouTube",
        ["set.social.instagram"] = "Instagram",
        ["set.social.github"] = "GitHub",
    },
    ["es"] = new()
    {
        ["set.social.title"] = "Redes sociales",
        ["set.social.subtitle"] = "Sigue a emirttac en redes sociales",
        ["set.social.youtube"] = "YouTube",
        ["set.social.instagram"] = "Instagram",
        ["set.social.github"] = "GitHub",
    },
    ["zh"] = new()
    {
        ["set.social.title"] = "社交媒体",
        ["set.social.subtitle"] = "关注 emirttac 的社交媒体账号",
        ["set.social.youtube"] = "YouTube",
        ["set.social.instagram"] = "Instagram",
        ["set.social.github"] = "GitHub",
    },
    ["fr"] = new()
    {
        ["set.social.title"] = "Réseaux sociaux",
        ["set.social.subtitle"] = "Suivez emirttac sur les réseaux sociaux",
        ["set.social.youtube"] = "YouTube",
        ["set.social.instagram"] = "Instagram",
        ["set.social.github"] = "GitHub",
    },
    ["ro"] = new()
    {
        ["set.social.title"] = "Social media",
        ["set.social.subtitle"] = "Urmărește-l pe emirttac pe rețelele sociale",
        ["set.social.youtube"] = "YouTube",
        ["set.social.instagram"] = "Instagram",
        ["set.social.github"] = "GitHub",
    },
    ["ru"] = new()
    {
        ["set.social.title"] = "Соцсети",
        ["set.social.subtitle"] = "Подписывайтесь на emirttac в соцсетях",
        ["set.social.youtube"] = "YouTube",
        ["set.social.instagram"] = "Instagram",
        ["set.social.github"] = "GitHub",
    },
};

var opts = new JsonSerializerOptions
{
    WriteIndented = true,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
};

foreach (var (lang, map) in additions)
{
    var path = Path.Combine(i18n, $"{lang}.json");
    using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
    var ordered = new Dictionary<string, string>();
    foreach (var p in doc.RootElement.EnumerateObject())
        ordered[p.Name] = p.Value.GetString() ?? "";
    foreach (var (k, v) in map)
        ordered[k] = v;
    File.WriteAllText(path, JsonSerializer.Serialize(ordered, opts) + Environment.NewLine, new UTF8Encoding(false));
    Console.WriteLine($"{lang} => {ordered.Count} keys");
}
