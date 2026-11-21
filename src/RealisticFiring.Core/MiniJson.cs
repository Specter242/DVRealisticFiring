using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RealisticFiring.Core
{
    internal enum JsonKind
    {
        Null,
        Bool,
        Number,
        String,
        Array,
        Object,
    }

    internal sealed class JsonValue
    {
        public JsonKind Kind { get; }
        public int Line { get; }
        public int Column { get; }
        public bool BoolValue { get; }
        public double NumberValue { get; }
        public string StringValue { get; }
        public IReadOnlyList<JsonValue> ArrayItems { get; }
        public IReadOnlyDictionary<string, JsonValue> ObjectProperties { get; }

        private JsonValue(JsonKind kind, int line, int column,
            bool boolValue = false, double numberValue = 0d, string stringValue = null,
            IReadOnlyList<JsonValue> arrayItems = null, IReadOnlyDictionary<string, JsonValue> objectProperties = null)
        {
            Kind = kind;
            Line = line;
            Column = column;
            BoolValue = boolValue;
            NumberValue = numberValue;
            StringValue = stringValue;
            ArrayItems = arrayItems;
            ObjectProperties = objectProperties;
        }

        public static JsonValue Null(int line, int column) => new JsonValue(JsonKind.Null, line, column);
        public static JsonValue Bool(bool value, int line, int column) => new JsonValue(JsonKind.Bool, line, column, boolValue: value);
        public static JsonValue Number(double value, int line, int column) => new JsonValue(JsonKind.Number, line, column, numberValue: value);
        public static JsonValue String(string value, int line, int column) => new JsonValue(JsonKind.String, line, column, stringValue: value);
        public static JsonValue Array(IReadOnlyList<JsonValue> items, int line, int column) => new JsonValue(JsonKind.Array, line, column, arrayItems: items);
        public static JsonValue Object(IReadOnlyDictionary<string, JsonValue> props, int line, int column) => new JsonValue(JsonKind.Object, line, column, objectProperties: props);
    }

    internal sealed class JsonParseException : Exception
    {
        public int Line { get; }
        public int Column { get; }

        public JsonParseException(string message, int line, int column)
            : base($"JSON syntax error at line {line}, column {column}: {message}")
        {
            Line = line;
            Column = column;
        }
    }

    /// <summary>
    /// Strict RFC 8259 JSON parser and canonical writer with zero external
    /// dependencies, keeping RealisticFiring.Core free of third-party libraries.
    /// </summary>
    internal sealed class MiniJsonParser
    {
        private const int MaxDepth = 32;

        private readonly string text;
        private int index;
        private int line = 1;
        private int column = 1;

        private MiniJsonParser(string text)
        {
            this.text = text;
            if (text.Length > 0 && text[0] == '\uFEFF')
                index = 1;
        }

        public static JsonValue Parse(string json)
        {
            if (json == null)
                throw new JsonParseException("input is null", 1, 1);
            var parser = new MiniJsonParser(json);
            parser.SkipWhitespace();
            if (parser.AtEnd)
                throw new JsonParseException("empty JSON document", parser.line, parser.column);
            var value = parser.ReadValue(0);
            parser.SkipWhitespace();
            if (!parser.AtEnd)
                throw new JsonParseException($"unexpected character '{parser.Peek}' after root value", parser.line, parser.column);
            return value;
        }

        private bool AtEnd => index >= text.Length;
        private char Peek => text[index];

        private char Advance()
        {
            var c = text[index++];
            if (c == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
            return c;
        }

        private void SkipWhitespace()
        {
            while (!AtEnd)
            {
                var c = Peek;
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
                    Advance();
                else
                    break;
            }
        }

        private JsonValue ReadValue(int depth)
        {
            if (depth > MaxDepth)
                throw new JsonParseException($"nesting exceeds maximum depth of {MaxDepth}", line, column);
            SkipWhitespace();
            if (AtEnd)
                throw new JsonParseException("unexpected end of input", line, column);
            var c = Peek;
            if (c == '{')
                return ReadObject(depth + 1);
            if (c == '[')
                return ReadArray(depth + 1);
            if (c == '"')
            {
                var startLine = line;
                var startCol = column;
                return JsonValue.String(ReadString(), startLine, startCol);
            }
            if (c == 't')
                return ReadLiteral("true", JsonValue.Bool(true, line, column));
            if (c == 'f')
                return ReadLiteral("false", JsonValue.Bool(false, line, column));
            if (c == 'n')
                return ReadLiteral("null", JsonValue.Null(line, column));
            if (c == '-' || (c >= '0' && c <= '9'))
                return ReadNumber();
            throw new JsonParseException($"unexpected character '{c}'", line, column);
        }

        private JsonValue ReadObject(int depth)
        {
            var startLine = line;
            var startCol = column;
            Advance(); // '{'
            var props = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
            SkipWhitespace();
            if (!AtEnd && Peek == '}')
            {
                Advance();
                return JsonValue.Object(props, startLine, startCol);
            }
            while (true)
            {
                SkipWhitespace();
                if (AtEnd)
                    throw new JsonParseException("unterminated object", startLine, startCol);
                if (Peek != '"')
                    throw new JsonParseException($"expected property name string, found '{Peek}'", line, column);
                var keyLine = line;
                var keyCol = column;
                var key = ReadString();
                if (props.ContainsKey(key))
                    throw new JsonParseException($"duplicate property '{key}'", keyLine, keyCol);
                SkipWhitespace();
                if (AtEnd || Peek != ':')
                    throw new JsonParseException($"expected ':' after property '{key}'", line, column);
                Advance();
                var value = ReadValue(depth);
                props[key] = value;
                SkipWhitespace();
                if (AtEnd)
                    throw new JsonParseException("unterminated object", startLine, startCol);
                if (Peek == '}')
                {
                    Advance();
                    return JsonValue.Object(props, startLine, startCol);
                }
                if (Peek != ',')
                    throw new JsonParseException($"expected ',' or '}}', found '{Peek}'", line, column);
                Advance();
            }
        }

        private JsonValue ReadArray(int depth)
        {
            var startLine = line;
            var startCol = column;
            Advance(); // '['
            var items = new List<JsonValue>();
            SkipWhitespace();
            if (!AtEnd && Peek == ']')
            {
                Advance();
                return JsonValue.Array(items, startLine, startCol);
            }
            while (true)
            {
                items.Add(ReadValue(depth));
                SkipWhitespace();
                if (AtEnd)
                    throw new JsonParseException("unterminated array", startLine, startCol);
                if (Peek == ']')
                {
                    Advance();
                    return JsonValue.Array(items, startLine, startCol);
                }
                if (Peek != ',')
                    throw new JsonParseException($"expected ',' or ']', found '{Peek}'", line, column);
                Advance();
            }
        }

        private string ReadString()
        {
            var startLine = line;
            var startCol = column;
            Advance(); // '"'
            var sb = new StringBuilder();
            while (!AtEnd)
            {
                var c = Advance();
                if (c == '"')
                    return sb.ToString();
                if (c < 0x20)
                    throw new JsonParseException("unescaped control character in string", line, column);
                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }
                if (AtEnd)
                    throw new JsonParseException("unterminated escape sequence", line, column);
                var esc = Advance();
                switch (esc)
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
                        sb.Append(ReadUnicodeEscape());
                        break;
                    default:
                        throw new JsonParseException($"invalid escape sequence '\\{esc}'", line, column);
                }
            }
            throw new JsonParseException("unterminated string", startLine, startCol);
        }

        private char ReadUnicodeEscape()
        {
            if (index + 4 > text.Length)
                throw new JsonParseException("incomplete \\uXXXX escape", line, column);
            var code = 0;
            for (var i = 0; i < 4; i++)
            {
                var c = Advance();
                int digit;
                if (c >= '0' && c <= '9') digit = c - '0';
                else if (c >= 'a' && c <= 'f') digit = 10 + (c - 'a');
                else if (c >= 'A' && c <= 'F') digit = 10 + (c - 'A');
                else throw new JsonParseException($"invalid hex digit '{c}' in \\uXXXX escape", line, column);
                code = (code << 4) | digit;
            }
            return (char)code;
        }

        private JsonValue ReadNumber()
        {
            var start = index;
            var startLine = line;
            var startCol = column;
            if (Peek == '-')
                Advance();
            if (AtEnd)
                throw new JsonParseException("incomplete number", startLine, startCol);
            if (Peek == '0')
            {
                Advance();
                if (!AtEnd && Peek >= '0' && Peek <= '9')
                    throw new JsonParseException("leading zeros are not allowed in JSON numbers", startLine, startCol);
            }
            else if (Peek >= '1' && Peek <= '9')
            {
                while (!AtEnd && Peek >= '0' && Peek <= '9')
                    Advance();
            }
            else
            {
                throw new JsonParseException($"invalid character '{Peek}' in number", line, column);
            }
            if (!AtEnd && Peek == '.')
            {
                Advance();
                if (AtEnd || Peek < '0' || Peek > '9')
                    throw new JsonParseException("expected digit after decimal point", line, column);
                while (!AtEnd && Peek >= '0' && Peek <= '9')
                    Advance();
            }
            if (!AtEnd && (Peek == 'e' || Peek == 'E'))
            {
                Advance();
                if (!AtEnd && (Peek == '+' || Peek == '-'))
                    Advance();
                if (AtEnd || Peek < '0' || Peek > '9')
                    throw new JsonParseException("expected digit in exponent", line, column);
                while (!AtEnd && Peek >= '0' && Peek <= '9')
                    Advance();
            }
            var raw = text.Substring(start, index - start);
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                || double.IsNaN(parsed) || double.IsInfinity(parsed))
                throw new JsonParseException($"number '{raw}' is out of range", startLine, startCol);
            return JsonValue.Number(parsed, startLine, startCol);
        }

        private JsonValue ReadLiteral(string literal, JsonValue value)
        {
            var startLine = line;
            var startCol = column;
            for (var i = 0; i < literal.Length; i++)
            {
                if (AtEnd || Advance() != literal[i])
                    throw new JsonParseException($"expected '{literal}'", startLine, startCol);
            }
            return value;
        }
    }

    internal sealed class MiniJsonWriter
    {
        private readonly StringBuilder sb = new StringBuilder();
        private readonly Stack<Container> stack = new Stack<Container>();
        private readonly bool exactNumbers;
        private bool expectValue;

        public MiniJsonWriter(bool exactNumbers = false)
        {
            this.exactNumbers = exactNumbers;
        }

        private sealed class Container
        {
            public bool IsArray;
            public bool Inline;
            public int Count;
        }

        public void WriteStartObject()
        {
            BeforeValue();
            sb.Append('{');
            stack.Push(new Container { IsArray = false, Inline = false });
        }

        public void WriteEndObject()
        {
            var c = stack.Pop();
            if (c.Count > 0)
            {
                sb.Append('\n');
                Indent();
            }
            sb.Append('}');
        }

        public void WriteStartArray(bool inline = false)
        {
            BeforeValue();
            sb.Append('[');
            stack.Push(new Container { IsArray = true, Inline = inline });
        }

        public void WriteEndArray()
        {
            var c = stack.Pop();
            if (c.Count > 0 && !c.Inline)
            {
                sb.Append('\n');
                Indent();
            }
            sb.Append(']');
        }

        public void WritePropertyName(string name)
        {
            var c = stack.Peek();
            if (c.Count > 0)
                sb.Append(',');
            sb.Append('\n');
            Indent();
            WriteEscaped(name);
            sb.Append(": ");
            c.Count++;
            expectValue = true;
        }

        public void WriteString(string value)
        {
            BeforeValue();
            if (value == null)
                sb.Append("null");
            else
                WriteEscaped(value);
        }

        public void WriteBool(bool value)
        {
            BeforeValue();
            sb.Append(value ? "true" : "false");
        }

        public void WriteInt(int value)
        {
            BeforeValue();
            sb.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        public void WriteNumber(float value, string format = "0.###")
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value), "JSON numbers must be finite.");
            BeforeValue();
            if (value == 0f)
                value = 0f;
            var text = exactNumbers
                ? value.ToString("G9", CultureInfo.InvariantCulture)
                : value.ToString(format, CultureInfo.InvariantCulture);
            if (!exactNumbers && text.StartsWith("-", StringComparison.Ordinal) && float.Parse(text, CultureInfo.InvariantCulture) == 0f)
                text = text.Substring(1);
            sb.Append(text);
        }

        public void WriteProperty(string name, string value)
        {
            WritePropertyName(name);
            WriteString(value);
        }

        public void WriteProperty(string name, bool value)
        {
            WritePropertyName(name);
            WriteBool(value);
        }

        public void WriteProperty(string name, int value)
        {
            WritePropertyName(name);
            WriteInt(value);
        }

        public void WriteProperty(string name, float value, string format = "0.###")
        {
            WritePropertyName(name);
            WriteNumber(value, format);
        }

        public void WriteVec2(string name, MakerFitVec2 value, string format = "0.###")
        {
            WritePropertyName(name);
            WriteStartArray(inline: true);
            WriteNumber(value.X, format);
            WriteNumber(value.Y, format);
            WriteEndArray();
        }

        public void WriteVec3(string name, MakerFitVec3 value, string format = "0.###")
        {
            WritePropertyName(name);
            WriteStartArray(inline: true);
            WriteNumber(value.X, format);
            WriteNumber(value.Y, format);
            WriteNumber(value.Z, format);
            WriteEndArray();
        }

        public override string ToString() => sb.ToString();

        private void BeforeValue()
        {
            if (expectValue)
            {
                expectValue = false;
                return;
            }
            if (stack.Count == 0)
                return;
            var c = stack.Peek();
            if (!c.IsArray)
                return;
            if (c.Count > 0)
                sb.Append(c.Inline ? ", " : ",");
            if (!c.Inline)
            {
                sb.Append('\n');
                Indent();
            }
            c.Count++;
        }

        private void Indent()
        {
            for (var i = 0; i < stack.Count; i++)
                sb.Append("  ");
        }

        private void WriteEscaped(string value)
        {
            sb.Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            sb.AppendFormat(CultureInfo.InvariantCulture, "\\u{0:x4}", (int)c);
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
