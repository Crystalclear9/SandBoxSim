using System.Collections.Generic;
using System.Text;

namespace SandBoxSim.Core.Foundation;

public sealed class JsonParseException : System.Exception
{
    public int Line { get; }
    public int Column { get; }

    public JsonParseException(string message, int line, int column)
        : base(message + " (line " + line + ", column " + column + ")")
    {
        Line = line;
        Column = column;
    }
}

/// <summary>
/// 递归下降 JSON 解析器。比 System.Text.Json 少 90% 的功能，但：
///   * 零依赖，可在 csc 降级通道下编译；
///   * 容忍 // 与 /* */ 注释（配置文件要给人读）；
///   * 报错带行列号，配置写错时不用猜。
/// </summary>
public static class JsonParser
{
    public static JsonValue Parse(string text)
    {
        var state = new State(text);
        state.SkipWhitespaceAndComments();
        JsonValue value = ParseValue(state, 0);
        state.SkipWhitespaceAndComments();
        if (!state.AtEnd)
        {
            throw new JsonParseException("JSON 根值之后还有多余内容", state.Line, state.Column);
        }
        return value;
    }

    private const int MaxDepth = 64;

    private sealed class State
    {
        public readonly string Text;
        public int Index;
        public int Line = 1;
        public int Column = 1;

        public State(string text) => Text = text ?? string.Empty;

        public bool AtEnd => Index >= Text.Length;

        public char Peek()
        {
            if (AtEnd) { throw new JsonParseException("JSON 意外结束", Line, Column); }
            return Text[Index];
        }

        public char PeekAt(int offset)
        {
            int i = Index + offset;
            return i < Text.Length ? Text[i] : '\0';
        }

        public char Next()
        {
            if (AtEnd) { throw new JsonParseException("JSON 意外结束", Line, Column); }
            char c = Text[Index++];
            if (c == '\n') { Line++; Column = 1; }
            else { Column++; }
            return c;
        }

        public void SkipWhitespaceAndComments()
        {
            while (!AtEnd)
            {
                char c = Text[Index];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
                {
                    Index++;
                    if (c == '\n') { Line++; Column = 1; } else { Column++; }
                    continue;
                }
                if (c == '/' && PeekAt(1) == '/')
                {
                    while (!AtEnd && Text[Index] != '\n') { Index++; Column++; }
                    continue;
                }
                if (c == '/' && PeekAt(1) == '*')
                {
                    Index += 2;
                    Column += 2;
                    while (!AtEnd && !(Text[Index] == '*' && PeekAt(1) == '/'))
                    {
                        if (Text[Index] == '\n') { Line++; Column = 1; } else { Column++; }
                        Index++;
                    }
                    if (AtEnd) { throw new JsonParseException("块注释没有闭合", Line, Column); }
                    Index += 2;
                    Column += 2;
                    continue;
                }
                break;
            }
        }
    }

    private static JsonValue ParseValue(State s, int depth)
    {
        if (depth > MaxDepth) { throw new JsonParseException("JSON 嵌套过深", s.Line, s.Column); }
        s.SkipWhitespaceAndComments();
        if (s.AtEnd) { throw new JsonParseException("期望一个值，但已是结尾", s.Line, s.Column); }

        char c = s.Peek();
        switch (c)
        {
            case '{': return ParseObject(s, depth);
            case '[': return ParseArray(s, depth);
            case '"': return JsonValue.From(ParseString(s));
            case 't':
                Expect(s, "true");
                return JsonValue.From(true);
            case 'f':
                Expect(s, "false");
                return JsonValue.From(false);
            case 'n':
                Expect(s, "null");
                return JsonValue.Null();
            default:
                if (c == '-' || (c >= '0' && c <= '9')) { return JsonValue.From(ParseNumber(s)); }
                throw new JsonParseException("无法识别的值起始字符 '" + c + "'", s.Line, s.Column);
        }
    }

    private static void Expect(State s, string literal)
    {
        for (int i = 0; i < literal.Length; i++)
        {
            if (s.AtEnd || s.Peek() != literal[i])
            {
                throw new JsonParseException("期望字面量 " + literal, s.Line, s.Column);
            }
            s.Next();
        }
    }

    private static JsonValue ParseObject(State s, int depth)
    {
        var obj = JsonValue.Object();
        s.Next(); // '{'
        s.SkipWhitespaceAndComments();
        if (!s.AtEnd && s.Peek() == '}') { s.Next(); return obj; }

        while (true)
        {
            s.SkipWhitespaceAndComments();
            if (s.AtEnd) { throw new JsonParseException("对象没有闭合", s.Line, s.Column); }
            if (s.Peek() != '"') { throw new JsonParseException("对象的键必须是字符串", s.Line, s.Column); }
            string key = ParseString(s);
            s.SkipWhitespaceAndComments();
            if (s.AtEnd || s.Peek() != ':') { throw new JsonParseException("键之后缺少 ':'", s.Line, s.Column); }
            s.Next();
            JsonValue value = ParseValue(s, depth + 1);
            obj.Set(key, value);

            s.SkipWhitespaceAndComments();
            if (s.AtEnd) { throw new JsonParseException("对象没有闭合", s.Line, s.Column); }
            char next = s.Next();
            if (next == '}') { break; }
            if (next != ',') { throw new JsonParseException("对象成员之间需要 ','", s.Line, s.Column); }
        }
        return obj;
    }

    private static JsonValue ParseArray(State s, int depth)
    {
        var arr = JsonValue.Array();
        s.Next(); // '['
        s.SkipWhitespaceAndComments();
        if (!s.AtEnd && s.Peek() == ']') { s.Next(); return arr; }

        while (true)
        {
            JsonValue item = ParseValue(s, depth + 1);
            arr.Add(item);
            s.SkipWhitespaceAndComments();
            if (s.AtEnd) { throw new JsonParseException("数组没有闭合", s.Line, s.Column); }
            char next = s.Next();
            if (next == ']') { break; }
            if (next != ',') { throw new JsonParseException("数组元素之间需要 ','", s.Line, s.Column); }
        }
        return arr;
    }

    private static string ParseString(State s)
    {
        s.Next(); // 开引号
        var sb = new StringBuilder(32);
        while (true)
        {
            if (s.AtEnd) { throw new JsonParseException("字符串没有闭合", s.Line, s.Column); }
            char c = s.Next();
            if (c == '"') { break; }
            if (c == '\\')
            {
                if (s.AtEnd) { throw new JsonParseException("转义序列不完整", s.Line, s.Column); }
                char esc = s.Next();
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
                    {
                        int code = 0;
                        for (int i = 0; i < 4; i++)
                        {
                            if (s.AtEnd) { throw new JsonParseException("\\u 转义不完整", s.Line, s.Column); }
                            char h = s.Next();
                            int digit = HexDigit(h);
                            if (digit < 0) { throw new JsonParseException("\\u 后面需要 4 位十六进制", s.Line, s.Column); }
                            code = (code << 4) | digit;
                        }
                        sb.Append((char)code);
                        break;
                    }
                    default:
                        throw new JsonParseException("未知转义字符 '\\" + esc + "'", s.Line, s.Column);
                }
                continue;
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static int HexDigit(char c)
    {
        if (c >= '0' && c <= '9') { return c - '0'; }
        if (c >= 'a' && c <= 'f') { return (c - 'a') + 10; }
        if (c >= 'A' && c <= 'F') { return (c - 'A') + 10; }
        return -1;
    }

    private static double ParseNumber(State s)
    {
        int start = s.Index;
        if (!s.AtEnd && s.Peek() == '-') { s.Next(); }
        while (!s.AtEnd && char.IsDigit(s.Peek())) { s.Next(); }
        if (!s.AtEnd && s.Peek() == '.')
        {
            s.Next();
            while (!s.AtEnd && char.IsDigit(s.Peek())) { s.Next(); }
        }
        if (!s.AtEnd && (s.Peek() == 'e' || s.Peek() == 'E'))
        {
            s.Next();
            if (!s.AtEnd && (s.Peek() == '+' || s.Peek() == '-')) { s.Next(); }
            while (!s.AtEnd && char.IsDigit(s.Peek())) { s.Next(); }
        }

        string raw = s.Text.Substring(start, s.Index - start);
        if (!double.TryParse(raw, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double value))
        {
            throw new JsonParseException("数字格式非法：" + raw, s.Line, s.Column);
        }
        return value;
    }
}
