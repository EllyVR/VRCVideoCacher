using System.Collections.Frozen;
using System.Reflection;
using Jeek.Avalonia.Localization;
using Newtonsoft.Json.Linq;

namespace VRCVideoCacher.Languages;

public class EmbeddedJsonLocalizer : BaseLocalizer
{
    private FrozenDictionary<string, string> _languageStrings = new Dictionary<string, string>().ToFrozenDictionary();
#if !DEBUG
    private FrozenDictionary<string, string> _enLanguageStrings = new Dictionary<string, string>().ToFrozenDictionary();
    private bool _enLanguageLoaded;
#endif

    private const string prefix = "VRCVideoCacher.Languages.";
    private const string suffix = ".loc.json";

    public EmbeddedJsonLocalizer()
    {
        Reload();
        OnLanguageChanged();
        FireLanguageChanged();
    }

    public override void Reload()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resources = assembly.GetManifestResourceNames()
            .Where(r => r.StartsWith(prefix) && r.EndsWith(suffix))
            .ToList();

        foreach (var resourceName in resources)
        {
            var langId = resourceName[prefix.Length..^suffix.Length];
            _languages.Add(langId);
        }

        ValidateLanguage();
        _hasLoaded = true;
        UpdateDisplayLanguages();
    }

#if !DEBUG
    private void EnglishLanguageLoad(Assembly assembly)
    {
        if (_enLanguageLoaded) return;
        var resourceName = assembly.GetManifestResourceNames()
            .First(r => r.Equals($"{prefix}{FallbackLanguage}{suffix}"));

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        var json = JObject.Parse(reader.ReadToEnd());

        _enLanguageStrings = json.Properties()
            .ToDictionary(k => k.Name, v => v.Value?.ToString() ?? v.Name)
            .ToFrozenDictionary();

        _enLanguageLoaded = true;
    }
#endif

    protected override void OnLanguageChanged()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .First(r => r.Equals($"{prefix}{_language}{suffix}"));

#if !DEBUG
        EnglishLanguageLoad(assembly);
#endif

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        var json = JObject.Parse(reader.ReadToEnd());

        _languageStrings = json.Properties()
            .ToDictionary(k => k.Name, v => v.Value?.ToString() ?? v.Name)
            .ToFrozenDictionary();
    }

    public override string Get(string key)
    {
        if (!_hasLoaded)
        {
            Reload();
        }

        if (_languageStrings?.TryGetValue(key, out string? value) == true)
        {
            return value;
        }

#if !DEBUG
        if (_enLanguageStrings?.TryGetValue(key, out var enValue) == true)
        {
            return enValue;
        }
#endif

        return base.Language + ":" + key;
    }
}
