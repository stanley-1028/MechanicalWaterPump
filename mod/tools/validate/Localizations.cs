namespace MWP.Tools.Validate;

/// <summary>
/// Cross-checks the three shipped localization files against each other and against the
/// <c>MwpLocKeys</c> set parsed from Core (CONTRACT section 8, REQ-UI-04/05).
/// </summary>
internal sealed class Localizations
{
    private static readonly string[] Languages = { "enUS", "zhCN", "zhTW" };

    /// <summary>zhTW display names mandated by REQ-UI-04 ("機械飲水泵", deliberately not "機械水泵").</summary>
    private static readonly string[] Tiers = SourceFacts.TierTokens;
    private static readonly string[] Roman = { "I", "II", "III", "IV" };

    private readonly Report _report;
    private readonly SourceFacts _facts;
    private readonly Dictionary<string, Dictionary<string, string>> _texts = new(StringComparer.Ordinal);

    public Localizations(SourceFacts facts, Report report)
    {
        _facts = facts;
        _report = report;
    }

    public void Validate(string dataDir, IReadOnlyCollection<string> usedLocKeys)
    {
        var directory = Path.Combine(dataDir, "Localizations");
        foreach (var language in Languages)
        {
            var path = Path.Combine(directory, language + ".csv");
            if (!File.Exists(path))
            {
                _report.Error("localization", "Localizations/" + language + ".csv is missing");
                continue;
            }

            Load(language, path);
        }

        if (_texts.Count != Languages.Length)
        {
            return;
        }

        var required = _facts.LocKeys;
        if (required.Count == 0)
        {
            _report.Error("localization", "cannot verify coverage: MwpLocKeys could not be read from Core");
        }

        foreach (var language in Languages)
        {
            var texts = _texts[language];

            foreach (var key in required)
            {
                if (!texts.ContainsKey(key))
                {
                    _report.Error("localization", "Localizations/" + language + ".csv is missing MwpLocKeys key '" + key + "'");
                }
            }

            foreach (var key in texts.Keys)
            {
                if (!required.Contains(key, StringComparer.Ordinal))
                {
                    _report.Error("localization", "'" + key + "' in " + language
                                                  + ".csv does not come from MwpLocKeys (CONTRACT section 8)");
                }
            }

            foreach (var key in usedLocKeys)
            {
                if (!texts.ContainsKey(key))
                {
                    _report.Error("localization", "a blueprint references '" + key + "' but " + language + ".csv has no such key");
                }
            }
        }

        foreach (var key in required)
        {
            var present = Languages.Where(language => _texts[language].ContainsKey(key)).ToList();
            if (present.Count != Languages.Length)
            {
                continue;
            }

            var placeholders = Languages.ToDictionary(language => language, language => Placeholders(_texts[language][key]));

            var reference = placeholders[Languages[0]];
            foreach (var language in Languages)
            {
                if (!placeholders[language].SequenceEqual(reference))
                {
                    _report.Error("localization", "'" + key + "' placeholder mismatch: "
                                                  + Languages[0] + " has {" + string.Join(",", reference) + "} but "
                                                  + language + " has {" + string.Join(",", placeholders[language]) + "}");
                }

                var text = _texts[language][key];
                if (text.Length == 0)
                {
                    _report.Error("localization", "'" + key + "' has an empty " + language + " text");
                }
                else if (string.Equals(text, key, StringComparison.Ordinal))
                {
                    _report.Error("localization", "'" + key + "' still shows the raw key in " + language + ".csv (REQ-UI-05)");
                }
            }
        }

        for (var i = 0; i < Tiers.Length; i++)
        {
            var key = "Building.MWP.Pump." + Tiers[i] + ".DisplayName";
            var expected = "機械飲水泵 Mk " + Roman[i];
            if (_texts["zhTW"].TryGetValue(key, out var text) && !string.Equals(text, expected, StringComparison.Ordinal))
            {
                _report.Error("localization", "zhTW '" + key + "' must be '" + expected + "' (REQ-UI-04), found '" + text + "'");
            }
        }
    }

    private void Load(string language, string path)
    {
        var rows = Csv.Parse(File.ReadAllText(path));
        if (rows.Count == 0)
        {
            _report.Error("localization", "Localizations/" + language + ".csv is empty");
            return;
        }

        var header = rows[0];
        if (header.Length < 3 || header[0].Trim() != "ID" || header[1].Trim() != "Text" || header[2].Trim() != "Comment")
        {
            _report.Error("localization", "Localizations/" + language
                                          + ".csv header must be 'ID,Text,Comment' (found '"
                                          + string.Join(",", header) + "')");
        }

        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Length == 0 || (row.Length == 1 && row[0].Trim().Length == 0))
            {
                continue;
            }

            if (row.Length != 3)
            {
                _report.Error("localization", "Localizations/" + language + ".csv line " + (i + 1) + " has "
                                              + row.Length + " CSV fields; the game requires exactly 3 (ID,Text,Comment)");
                continue;
            }

            var id = row[0].Trim();
            if (id.Length == 0)
            {
                _report.Error("localization", "Localizations/" + language + ".csv line " + (i + 1) + " has an empty ID");
                continue;
            }

            if (!texts.TryAdd(id, row[1]))
            {
                _report.Error("localization", "duplicate key '" + id + "' in Localizations/" + language + ".csv line " + (i + 1));
            }
        }

        _texts[language] = texts;
    }

    private static List<int> Placeholders(string text)
    {
        var result = new List<int>();
        var index = 0;
        while (index < text.Length)
        {
            var start = text.IndexOf('{', index);
            if (start < 0)
            {
                break;
            }

            var end = text.IndexOf('}', start + 1);
            if (end < 0)
            {
                break;
            }

            var token = text.Substring(start + 1, end - start - 1);
            if (int.TryParse(token, out var number))
            {
                result.Add(number);
            }

            index = end + 1;
        }

        result.Sort();
        return result;
    }
}
