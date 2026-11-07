using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MWP.Core
{
    /// <summary>
    /// Compact JSON writer behind <see cref="MiniJson.Write"/>. Kept in its own file so both the
    /// reader and the writer stay reviewable under the 300-line rule (REQ-NFR-03).
    /// </summary>
    internal static class MiniJsonWriter
    {
        private const int MaxDepth = 64;

        /// <summary>Serializes a supported value tree to compact JSON text.</summary>
        internal static string Write(object? value)
        {
            var builder = new StringBuilder(256);
            WriteValue(builder, value, 0);
            return builder.ToString();
        }

        private static void WriteValue(StringBuilder builder, object? value, int depth)
        {
            if (depth > MaxDepth)
            {
                throw new FormatException("JSON nesting is too deep to write.");
            }

            switch (value)
            {
                case null:
                    builder.Append("null");
                    return;
                case string text:
                    WriteString(builder, text);
                    return;
                case bool flag:
                    builder.Append(flag ? "true" : "false");
                    return;
                case IDictionary<string, object?> map:
                    WriteObject(builder, map, depth);
                    return;
                case IList list:
                    WriteArray(builder, list, depth);
                    return;
                default:
                    WriteNumberOrThrow(builder, value);
                    return;
            }
        }

        /// <summary>Writes any supported numeric type or rejects the value.</summary>
        private static void WriteNumberOrThrow(StringBuilder builder, object value)
        {
            switch (value)
            {
                case double number:
                    WriteDouble(builder, number);
                    return;
                case float number:
                    WriteFloat(builder, number);
                    return;
                case decimal number:
                    builder.Append(number.ToString(CultureInfo.InvariantCulture));
                    return;
                case int number:
                    builder.Append(number.ToString(CultureInfo.InvariantCulture));
                    return;
                case long number:
                    builder.Append(number.ToString(CultureInfo.InvariantCulture));
                    return;
                case short number:
                    builder.Append(number.ToString(CultureInfo.InvariantCulture));
                    return;
                case byte number:
                    builder.Append(number.ToString(CultureInfo.InvariantCulture));
                    return;
                default:
                    throw new ArgumentException("Unsupported JSON value type: " + value.GetType().FullName);
            }
        }

        private static void WriteObject(StringBuilder builder, IDictionary<string, object?> map, int depth)
        {
            builder.Append('{');
            bool first = true;
            foreach (KeyValuePair<string, object?> pair in map)
            {
                if (!first)
                {
                    builder.Append(',');
                }

                first = false;
                WriteString(builder, pair.Key);
                builder.Append(':');
                WriteValue(builder, pair.Value, depth + 1);
            }

            builder.Append('}');
        }

        private static void WriteArray(StringBuilder builder, IList list, int depth)
        {
            builder.Append('[');
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                WriteValue(builder, list[i], depth + 1);
            }

            builder.Append(']');
        }

        private static void WriteDouble(StringBuilder builder, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new FormatException("JSON cannot represent NaN or Infinity.");
            }

            builder.Append(value.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void WriteFloat(StringBuilder builder, float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new FormatException("JSON cannot represent NaN or Infinity.");
            }

            builder.Append(value.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void WriteString(StringBuilder builder, string value)
        {
            builder.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            builder.Append("\\u");
                            builder.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(c);
                        }

                        break;
                }
            }

            builder.Append('"');
        }
    }
}
