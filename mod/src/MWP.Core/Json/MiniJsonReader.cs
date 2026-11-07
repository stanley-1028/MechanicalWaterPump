using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MWP.Core
{
    /// <summary>
    /// Recursive-descent JSON reader behind <see cref="MiniJson.Parse"/>. Split from the public API
    /// so both files stay under the 300-line rule (REQ-NFR-03).
    /// </summary>
    internal static partial class MiniJsonReader
    {
        /// <summary>Maximum nesting depth accepted; guards against stack exhaustion.</summary>
        private const int MaxDepth = 64;

        /// <summary>Parses one complete JSON document, tolerating a UTF-8 byte-order mark.</summary>
        internal static object? Parse(string text)
        {
            if (text.Length > 0 && text[0] == '\uFEFF')
            {
                text = text.Substring(1);
            }

            return new Reader(text).ReadDocument();
        }

        /// <summary>Reader state for one document.</summary>
        private sealed partial class Reader
        {
            private readonly string _text;
            private int _index;
            private int _depth;

            internal Reader(string text)
            {
                _text = text;
            }

            internal object? ReadDocument()
            {
                SkipWhitespace();
                object? value = ReadValue();
                SkipWhitespace();
                if (_index != _text.Length)
                {
                    throw Error("trailing content");
                }

                return value;
            }

            private object? ReadValue()
            {
                if (_index >= _text.Length)
                {
                    throw Error("unexpected end of input");
                }

                char c = _text[_index];
                switch (c)
                {
                    case '{': return ReadObject();
                    case '[': return ReadArray();
                    case '"': return ReadString();
                    case 't': ReadLiteral("true"); return true;
                    case 'f': ReadLiteral("false"); return false;
                    case 'n': ReadLiteral("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9'))
                        {
                            return ReadNumber();
                        }

                        throw Error("unexpected character '" + c + "'");
                }
            }

            private Dictionary<string, object?> ReadObject()
            {
                Enter();
                _index++;
                var result = new Dictionary<string, object?>(StringComparer.Ordinal);
                SkipWhitespace();
                if (Peek() == '}')
                {
                    _index++;
                    _depth--;
                    return result;
                }

                while (true)
                {
                    SkipWhitespace();
                    if (Peek() != '"')
                    {
                        throw Error("expected a property name");
                    }

                    string key = ReadString();
                    SkipWhitespace();
                    Expect(':');
                    SkipWhitespace();
                    result[key] = ReadValue();
                    SkipWhitespace();
                    char separator = Peek();
                    if (separator == ',')
                    {
                        _index++;
                        continue;
                    }

                    if (separator == '}')
                    {
                        _index++;
                        _depth--;
                        return result;
                    }

                    throw Error("expected ',' or '}'");
                }
            }

            private List<object?> ReadArray()
            {
                Enter();
                _index++;
                var result = new List<object?>();
                SkipWhitespace();
                if (Peek() == ']')
                {
                    _index++;
                    _depth--;
                    return result;
                }

                while (true)
                {
                    SkipWhitespace();
                    result.Add(ReadValue());
                    SkipWhitespace();
                    char separator = Peek();
                    if (separator == ',')
                    {
                        _index++;
                        continue;
                    }

                    if (separator == ']')
                    {
                        _index++;
                        _depth--;
                        return result;
                    }

                    throw Error("expected ',' or ']'");
                }
            }

            private double ReadNumber()
            {
                int start = _index;
                while (_index < _text.Length && IsNumberChar(_text[_index]))
                {
                    _index++;
                }

                string token = _text.Substring(start, _index - start);
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                {
                    throw Error("invalid number '" + token + "'");
                }

                return value;
            }

            private static bool IsNumberChar(char c)
            {
                return (c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E';
            }

            private void ReadLiteral(string literal)
            {
                if (_index + literal.Length > _text.Length
                    || string.CompareOrdinal(_text, _index, literal, 0, literal.Length) != 0)
                {
                    throw Error("invalid literal");
                }

                _index += literal.Length;
            }

            private void Enter()
            {
                _depth++;
                if (_depth > MaxDepth)
                {
                    throw Error("nesting too deep");
                }
            }

            private char Peek()
            {
                if (_index >= _text.Length)
                {
                    throw Error("unexpected end of input");
                }

                return _text[_index];
            }

            private void Expect(char expected)
            {
                if (Peek() != expected)
                {
                    throw Error("expected '" + expected + "'");
                }

                _index++;
            }

            private void SkipWhitespace()
            {
                while (_index < _text.Length)
                {
                    char c = _text[_index];
                    if (c != ' ' && c != '\t' && c != '\r' && c != '\n')
                    {
                        return;
                    }

                    _index++;
                }
            }

            private FormatException Error(string reason)
            {
                return new FormatException(
                    "Invalid JSON at index " + _index.ToString(CultureInfo.InvariantCulture) + ": " + reason);
            }
        }
    }
}
