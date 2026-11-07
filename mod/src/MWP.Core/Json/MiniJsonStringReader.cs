using System;
using System.Text;

namespace MWP.Core
{
    /// <summary>String/escape half of the JSON reader (partial with the main reader for the 300-line rule).</summary>
    internal static partial class MiniJsonReader
    {
        /// <summary>Reads one quoted JSON string, including escape sequences.</summary>
        private sealed partial class Reader
        {
            private string ReadString()
            {
                _index++;
                var builder = new StringBuilder(32);
                while (true)
                {
                    if (_index >= _text.Length)
                    {
                        throw Error("unterminated string");
                    }

                    char c = _text[_index++];
                    if (c == '"')
                    {
                        return builder.ToString();
                    }

                    if (c == '\\')
                    {
                        AppendEscape(builder);
                        continue;
                    }

                    if (c < 0x20)
                    {
                        throw Error("unescaped control character");
                    }

                    builder.Append(c);
                }
            }

            private void AppendEscape(StringBuilder builder)
            {
                if (_index >= _text.Length)
                {
                    throw Error("unterminated escape");
                }

                char escape = _text[_index++];
                switch (escape)
                {
                    case '"': builder.Append('"'); return;
                    case '\\': builder.Append('\\'); return;
                    case '/': builder.Append('/'); return;
                    case 'b': builder.Append('\b'); return;
                    case 'f': builder.Append('\f'); return;
                    case 'n': builder.Append('\n'); return;
                    case 'r': builder.Append('\r'); return;
                    case 't': builder.Append('\t'); return;
                    case 'u': builder.Append((char)ReadHex4()); return;
                    default: throw Error("invalid escape '\\" + escape + "'");
                }
            }

            private int ReadHex4()
            {
                if (_index + 4 > _text.Length)
                {
                    throw Error("truncated unicode escape");
                }

                int value = 0;
                for (int i = 0; i < 4; i++)
                {
                    int digit = HexDigit(_text[_index++]);
                    if (digit < 0)
                    {
                        throw Error("invalid unicode escape");
                    }

                    value = (value << 4) | digit;
                }

                return value;
            }

            private static int HexDigit(char c)
            {
                if (c >= '0' && c <= '9')
                {
                    return c - '0';
                }

                if (c >= 'a' && c <= 'f')
                {
                    return c - 'a' + 10;
                }

                return c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;
            }
        }
    }
}
