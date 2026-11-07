using System.Text;

namespace MWP.Tools.Validate;

/// <summary>Collects validation problems. Warnings do not change the exit code.</summary>
internal sealed class Report
{
    private readonly List<string> _errors = new();
    private readonly List<string> _warnings = new();

    public int ErrorCount => _errors.Count;

    public int WarningCount => _warnings.Count;

    public void Error(string category, string message) => _errors.Add("[" + category + "] " + message);

    public void Warn(string category, string message) => _warnings.Add("[" + category + "] " + message);

    /// <summary>Prints the report. Returns the process exit code (0 = pass).</summary>
    public int Finish(TextWriter output)
    {
        output.WriteLine();

        if (_warnings.Count == 0)
        {
            output.WriteLine("WARN: none");
        }
        else
        {
            foreach (var warning in _warnings)
            {
                output.WriteLine("WARN: " + warning);
            }
        }

        output.WriteLine();

        if (_errors.Count == 0)
        {
            output.WriteLine("VALIDATION PASSED");
            return 0;
        }

        foreach (var error in _errors)
        {
            output.WriteLine("ERROR: " + error);
        }

        output.WriteLine();
        output.WriteLine("VALIDATION FAILED (" + _errors.Count + " error(s), " + _warnings.Count + " warning(s))");
        return 1;
    }
}

/// <summary>Minimal RFC 4180 CSV reader, enough for the game's localization files.</summary>
internal static class Csv
{
    public static List<string[]> Parse(string text)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var index = 0;

        if (text.Length > 0 && text[0] == '\uFEFF')
        {
            index = 1;
        }

        while (index < text.Length)
        {
            var ch = text[index];

            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (index + 1 < text.Length && text[index + 1] == '"')
                    {
                        field.Append('"');
                        index += 2;
                        continue;
                    }

                    inQuotes = false;
                    index++;
                    continue;
                }

                field.Append(ch);
                index++;
                continue;
            }

            switch (ch)
            {
                case '"':
                    inQuotes = true;
                    index++;
                    break;

                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    index++;
                    break;

                case '\r':
                    index++;
                    break;

                case '\n':
                    row.Add(field.ToString());
                    field.Clear();
                    rows.Add(row.ToArray());
                    row.Clear();
                    index++;
                    break;

                default:
                    field.Append(ch);
                    index++;
                    break;
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row.ToArray());
        }

        return rows;
    }
}
