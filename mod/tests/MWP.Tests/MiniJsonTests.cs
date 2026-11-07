using System;
using System.Collections.Generic;
using MWP.Core;

namespace MWP.Tests
{
    // REQ-NFR-03 note: the 40-line cap is a soft guideline for logic. The group methods in
    // this file are declarative lists of independent t.Run cases; splitting them further
    // would only scatter the cases, so the documented reason for exceeding it is recorded here.
    /// <summary>Unit tests for <see cref="MiniJson"/>: roundtrip, escaping, typed getters and invalid input.</summary>
    internal static class MiniJsonTests
    {
        internal static void Run(TestRunner t)
        {
            RoundtripTests(t);
            WriterTests(t);
            InvalidInputTests(t);
            BomAndGetterTests(t);

        }

        private static void GetterTests(TestRunner t)
        {
            var map = new Dictionary<string, object?>
            {
                ["number"] = 3.5,
                ["whole"] = 4.0,
                ["flag"] = true,
                ["text"] = "value",
                ["Id"] = "Log"
            };

            t.Run("GetNumber reads a double", () =>
                Assert.Near(3.5, MiniJson.GetNumber(map, "number", 0.0), 1e-12, "number"));
            t.Run("GetNumber falls back for a missing key", () =>
                Assert.Near(7.0, MiniJson.GetNumber(map, "missing", 7.0), 1e-12, "missing"));
            t.Run("GetNumber falls back for a wrong type", () =>
                Assert.Near(9.0, MiniJson.GetNumber(map, "text", 9.0), 1e-12, "wrong type"));
            t.Run("GetInt truncates", () =>
                Assert.Equal(3, MiniJson.GetInt(map, "number", -1), "truncate"));
            t.Run("GetInt reads a whole number", () =>
                Assert.Equal(4, MiniJson.GetInt(map, "whole", -1), "whole"));
            t.Run("GetInt falls back for a missing key", () =>
                Assert.Equal(5, MiniJson.GetInt(map, "missing", 5), "missing int"));
            t.Run("GetBool reads a boolean", () =>
                Assert.True(MiniJson.GetBool(map, "flag", false), "bool"));
            t.Run("GetBool falls back for a wrong type", () =>
                Assert.True(MiniJson.GetBool(map, "number", true), "bool fallback"));
            t.Run("GetString reads a string", () =>
                Assert.Equal("value", MiniJson.GetString(map, "text", string.Empty), "string"));
            t.Run("GetString falls back for a missing key", () =>
                Assert.Equal("fallback", MiniJson.GetString(map, "missing", "fallback"), "string fallback"));
            t.Run("Getters match keys case-insensitively", () =>
                Assert.Equal("Log", MiniJson.GetString(map, "id", string.Empty), "case-insensitive"));
        }
        /// <summary>Test group: RoundtripTests.</summary>
        private static void RoundtripTests(TestRunner t)
        {
            t.Run("Roundtrip of a nested document", () =>
            {
                var document = new Dictionary<string, object?>
                {
                    ["name"] = "機械飲水泵 Mk I",
                    ["rate"] = 3.0,
                    ["enabled"] = false,
                    ["items"] = new List<object?> { 1.0, "two", null, true },
                    ["nested"] = new Dictionary<string, object?> { ["x"] = 0.5 }
                };

                string json = MiniJson.Write(document);
                var parsed = MiniJson.Parse(json) as IDictionary<string, object?>;
                Assert.NotNull(parsed, "parsed root");
                Assert.Equal("機械飲水泵 Mk I", MiniJson.GetString(parsed!, "name", string.Empty), "string value");
                Assert.Near(3.0, MiniJson.GetNumber(parsed!, "rate", 0.0), 1e-12, "number value");
                Assert.False(MiniJson.GetBool(parsed!, "enabled", true), "bool value");

                var items = parsed!["items"] as IList<object?>;
                Assert.NotNull(items, "items list");
                Assert.Near(1.0, (double)items![0]!, 1e-12, "item number");
                Assert.Equal("two", items[1] as string, "item string");
                Assert.True(items[2] == null, "item null");

                var nested = parsed!["nested"] as IDictionary<string, object?>;
                Assert.NotNull(nested, "nested object");
                Assert.Near(0.5, MiniJson.GetNumber(nested!, "x", 0.0), 1e-12, "nested number");
            });

            t.Run("Roundtrip of escaped characters", () =>
            {
                const string original = "quote\" backslash\\ newline\n tab\t slash/";
                string json = MiniJson.Write(original);
                Assert.Equal(original, MiniJson.Parse(json) as string, "escape roundtrip");
            });

            t.Run("Control characters are written as unicode escapes", () =>
            {
                string json = MiniJson.Write("\u0001");
                Assert.Contains(json, "\\u0001", "escaped control char");
                Assert.Equal("\u0001", MiniJson.Parse(json) as string, "control roundtrip");
            });

            t.Run("Unicode escapes are parsed", () =>
            {
                Assert.Equal("機", MiniJson.Parse("\"\\u6a5f\"") as string, "unicode escape");
            });

            t.Run("Numbers with exponent are parsed", () =>
            {
                object? value = MiniJson.Parse("1e3");
                Assert.Near(1000.0, (double)value!, 1e-12, "exponent number");
            });

            t.Run("Scientific and negative numbers roundtrip", () =>
            {
                object? value = MiniJson.Parse(MiniJson.Write(-1.5e-2));
                Assert.Near(-0.015, (double)value!, 1e-15, "negative exponent");
            });

            t.Run("Writer emits null and booleans", () =>
            {
                Assert.Equal("null", MiniJson.Write(null), "null");
                Assert.Equal("true", MiniJson.Write(true), "true");
                Assert.Equal("false", MiniJson.Write(false), "false");
            });
        }

        /// <summary>Test group: WriterTests.</summary>
        private static void WriterTests(TestRunner t)
        {
            t.Run("Writer rejects NaN", () =>
                Assert.Throws<FormatException>(() => MiniJson.Write(double.NaN), "NaN"));
            t.Run("Writer rejects Infinity", () =>
                Assert.Throws<FormatException>(() => MiniJson.Write(double.PositiveInfinity), "Inf"));
            t.Run("Writer rejects unsupported types", () =>
                Assert.Throws<ArgumentException>(() => MiniJson.Write(new object()), "object"));
        }

        /// <summary>Test group: InvalidInputTests.</summary>
        private static void InvalidInputTests(TestRunner t)
        {
            t.Run("Parse rejects empty text", () =>
                Assert.Throws<FormatException>(() => MiniJson.Parse(string.Empty), "empty"));
            t.Run("Parse rejects an unterminated object", () =>
                Assert.Throws<FormatException>(() => MiniJson.Parse("{"), "unterminated"));
            t.Run("Parse rejects a trailing comma", () =>
                Assert.Throws<FormatException>(() => MiniJson.Parse("[1,]"), "trailing comma"));
            t.Run("Parse rejects trailing content", () =>
                Assert.Throws<FormatException>(() => MiniJson.Parse("{}x"), "trailing"));
            t.Run("Parse rejects a broken literal", () =>
                Assert.Throws<FormatException>(() => MiniJson.Parse("tru"), "literal"));
            t.Run("Parse rejects a leading plus", () =>
                Assert.Throws<FormatException>(() => MiniJson.Parse("+1"), "plus"));
            t.Run("Parse rejects null text", () =>
                Assert.Throws<ArgumentNullException>(() => MiniJson.Parse(null!), "null text"));
            t.Run("Parse rejects excessive nesting", () =>
                Assert.Throws<FormatException>(
                    () => MiniJson.Parse(new string('[', 70) + new string(']', 70)),
                    "deep nesting"));
        }

        /// <summary>Test group: BomAndGetterTests.</summary>
        private static void BomAndGetterTests(TestRunner t)
        {
            t.Run("Parse tolerates a UTF-8 BOM", () =>
            {
                var parsed = MiniJson.Parse("\uFEFF{\"a\":1}") as IDictionary<string, object?>;
                Assert.NotNull(parsed, "BOM document");
                Assert.Near(1.0, MiniJson.GetNumber(parsed!, "a", 0.0), 1e-12, "BOM value");
            });

            GetterTests(t);
        }
    }
}
