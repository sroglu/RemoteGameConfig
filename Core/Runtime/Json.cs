using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PFound.RemoteGameConfig.Core
{
    enum JsonKind
    {
        Object,
        Array,
        String,
        Number,
        Bool,
        Null
    }

    /// <summary>
    /// A minimal, engine-free JSON tree. RemoteGameConfig cannot depend on UnityEngine.JsonUtility (the
    /// Core is pure C# and must run under mono/csc) and pulls in no third-party JSON library, so it
    /// carries this small reader. It exists only to turn a config document's bytes into the typed
    /// <see cref="ConfigDocument"/> model; it is not a general-purpose serializer.
    /// </summary>
    sealed class JsonValue
    {
        public JsonKind Kind;
        public Dictionary<string, JsonValue> Members;
        public List<JsonValue> Items;
        public string Text;
        public bool Boolean;

        public bool IsObject => Kind == JsonKind.Object;
        public bool IsArray => Kind == JsonKind.Array;

        public bool TryMember(string name, out JsonValue value)
        {
            if (Kind == JsonKind.Object) return Members.TryGetValue(name, out value);
            value = null;
            return false;
        }

        /// <summary>Interpret this scalar as a <see cref="ConfigValue"/>: bool, integer, float, or string.</summary>
        public ConfigValue ToConfigValue()
        {
            switch (Kind)
            {
                case JsonKind.Bool:
                    return ConfigValue.Of(Boolean);
                case JsonKind.String:
                    return ConfigValue.Of(Text);
                case JsonKind.Number:
                    // No '.'/'e' ⇒ integer; otherwise a float. Keeps "5" an int and "1.5" a float.
                    if (Text.IndexOf('.') < 0 && Text.IndexOf('e') < 0 && Text.IndexOf('E') < 0
                        && long.TryParse(Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long asLong))
                    {
                        return ConfigValue.Of(asLong);
                    }
                    double asDouble = double.Parse(Text, NumberStyles.Float, CultureInfo.InvariantCulture);
                    return ConfigValue.Of(asDouble);
                default:
                    return ConfigValue.Of(Text ?? string.Empty);
            }
        }
    }

    /// <summary>Signals malformed JSON; only ever thrown inside <see cref="JsonParser.TryParse"/> and turned into a false result there.</summary>
    sealed class JsonParseException : System.Exception
    {
        public JsonParseException(string message) : base(message) { }
    }

    /// <summary>
    /// A small recursive-descent JSON reader (objects, arrays, strings with escapes, numbers, bool,
    /// null). It never lets a parse fault escape as a raw exception to the caller: malformed input is
    /// reported as a false return from <see cref="TryParse"/> so a corrupt remote document is ignored
    /// rather than crashing the game.
    /// </summary>
    static class JsonParser
    {
        public static bool TryParse(string text, out JsonValue root)
        {
            root = null;
            // Parsing external, untrusted document bytes into a typed tree is an IO-boundary translation:
            // the throw is the parser's failure channel, caught here and turned into a data result.
            try
            {
                int index = 0;
                JsonValue value = ParseValue(text, ref index);
                SkipWhitespace(text, ref index);
                if (index != text.Length) throw new JsonParseException("Trailing characters after JSON value.");
                root = value;
                return true;
            }
            catch (JsonParseException)
            {
                return false;
            }
        }

        static JsonValue ParseValue(string s, ref int i)
        {
            SkipWhitespace(s, ref i);
            if (i >= s.Length) throw new JsonParseException("Unexpected end of input.");
            char c = s[i];
            switch (c)
            {
                case '{': return ParseObject(s, ref i);
                case '[': return ParseArray(s, ref i);
                case '"': return new JsonValue { Kind = JsonKind.String, Text = ParseString(s, ref i) };
                case 't':
                case 'f': return ParseBool(s, ref i);
                case 'n': ParseLiteral(s, ref i, "null"); return new JsonValue { Kind = JsonKind.Null };
                default: return ParseNumber(s, ref i);
            }
        }

        static JsonValue ParseObject(string s, ref int i)
        {
            var value = new JsonValue { Kind = JsonKind.Object, Members = new Dictionary<string, JsonValue>() };
            i++; // '{'
            SkipWhitespace(s, ref i);
            if (Peek(s, i) == '}') { i++; return value; }
            while (true)
            {
                SkipWhitespace(s, ref i);
                if (Peek(s, i) != '"') throw new JsonParseException("Expected object key.");
                string key = ParseString(s, ref i);
                SkipWhitespace(s, ref i);
                if (Peek(s, i) != ':') throw new JsonParseException("Expected ':' after object key.");
                i++;
                value.Members[key] = ParseValue(s, ref i);
                SkipWhitespace(s, ref i);
                char next = Peek(s, i);
                if (next == ',') { i++; continue; }
                if (next == '}') { i++; break; }
                throw new JsonParseException("Expected ',' or '}' in object.");
            }
            return value;
        }

        static JsonValue ParseArray(string s, ref int i)
        {
            var value = new JsonValue { Kind = JsonKind.Array, Items = new List<JsonValue>() };
            i++; // '['
            SkipWhitespace(s, ref i);
            if (Peek(s, i) == ']') { i++; return value; }
            while (true)
            {
                value.Items.Add(ParseValue(s, ref i));
                SkipWhitespace(s, ref i);
                char next = Peek(s, i);
                if (next == ',') { i++; continue; }
                if (next == ']') { i++; break; }
                throw new JsonParseException("Expected ',' or ']' in array.");
            }
            return value;
        }

        static string ParseString(string s, ref int i)
        {
            i++; // opening quote
            var sb = new StringBuilder();
            while (true)
            {
                if (i >= s.Length) throw new JsonParseException("Unterminated string.");
                char c = s[i++];
                if (c == '"') break;
                if (c == '\\')
                {
                    if (i >= s.Length) throw new JsonParseException("Unterminated escape.");
                    char e = s[i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 > s.Length) throw new JsonParseException("Truncated unicode escape.");
                            int code = int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                            sb.Append((char)code);
                            i += 4;
                            break;
                        default: throw new JsonParseException("Invalid escape character.");
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        static JsonValue ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length)
            {
                char c = s[i];
                bool numeric = (c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E';
                if (!numeric) break;
                i++;
            }
            if (i == start) throw new JsonParseException("Expected a value.");
            string token = s.Substring(start, i - start);
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double _))
            {
                throw new JsonParseException("Malformed number.");
            }
            return new JsonValue { Kind = JsonKind.Number, Text = token };
        }

        static JsonValue ParseBool(string s, ref int i)
        {
            if (Peek(s, i) == 't') { ParseLiteral(s, ref i, "true"); return new JsonValue { Kind = JsonKind.Bool, Boolean = true }; }
            ParseLiteral(s, ref i, "false");
            return new JsonValue { Kind = JsonKind.Bool, Boolean = false };
        }

        static void ParseLiteral(string s, ref int i, string literal)
        {
            if (i + literal.Length > s.Length || s.Substring(i, literal.Length) != literal)
            {
                throw new JsonParseException("Invalid literal.");
            }
            i += literal.Length;
        }

        static void SkipWhitespace(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r') i++;
                else break;
            }
        }

        static char Peek(string s, int i) => i < s.Length ? s[i] : '\0';
    }
}
