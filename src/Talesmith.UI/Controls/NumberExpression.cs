using System.Globalization;

namespace Talesmith.UI.Controls;

/// <summary>Evaluates the arithmetic typed into numeric fields, such as <c>16*2+4</c> or <c>(1 - 0.25) / 2</c>.</summary>
/// <remarks>Supports <c>+ - * / % ^</c>, parentheses and unary signs. Both <c>.</c> and <c>,</c> are accepted as decimal separators.</remarks>
public static class NumberExpression
{
    /// <summary>Evaluates <paramref name="text"/>; fails on syntax errors and on results that are not finite.</summary>
    public static bool TryEvaluate(string? text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var parser = new Parser(text);
        if (!parser.TryExpression(out var result) || !parser.AtEnd || !double.IsFinite(result))
            return false;

        value = result;
        return true;
    }

    private ref struct Parser(ReadOnlySpan<char> text)
    {
        private readonly ReadOnlySpan<char> _text = text;
        private int _position;

        public bool AtEnd
        {
            get
            {
                SkipWhitespace();
                return _position >= _text.Length;
            }
        }

        public bool TryExpression(out double value)
        {
            if (!TryTerm(out value))
                return false;

            while (TryConsume('+', '-', out var op))
            {
                if (!TryTerm(out var right))
                    return false;
                value = op == '+' ? value + right : value - right;
            }

            return true;
        }

        private bool TryTerm(out double value)
        {
            if (!TryUnary(out value))
                return false;

            while (TryConsume('*', '/', '%', out var op))
            {
                if (!TryUnary(out var right))
                    return false;
                value = op switch
                {
                    '*' => value * right,
                    '/' => value / right,
                    _ => value % right
                };
            }

            return true;
        }

        private bool TryUnary(out double value)
        {
            if (TryConsume('-', '+', out var sign))
            {
                if (!TryUnary(out value))
                    return false;
                if (sign == '-')
                    value = -value;
                return true;
            }

            return TryPower(out value);
        }

        private bool TryPower(out double value)
        {
            if (!TryPrimary(out value))
                return false;
            if (!TryConsume('^', '^', out _))
                return true;
            if (!TryUnary(out var exponent))
                return false;
            value = Math.Pow(value, exponent);
            return true;
        }

        private bool TryPrimary(out double value)
        {
            value = 0;
            if (TryConsume('(', '(', out _))
                return TryExpression(out value) && TryConsume(')', ')', out _);

            SkipWhitespace();
            var start = _position;
            var seenSeparator = false;
            while (_position < _text.Length)
            {
                var c = _text[_position];
                if (char.IsAsciiDigit(c))
                {
                    _position++;
                }
                else if (c is '.' or ',' && !seenSeparator)
                {
                    seenSeparator = true;
                    _position++;
                }
                else
                {
                    break;
                }
            }

            if (_position == start)
                return false;

            Span<char> buffer = stackalloc char[_position - start];
            _text[start.._position].CopyTo(buffer);
            buffer.Replace(',', '.');
            if (buffer is ['.'])
                return false;
            return double.TryParse(buffer, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
        }

        private bool TryConsume(char a, char b, out char consumed) => TryConsume(a, b, b, out consumed);

        private bool TryConsume(char a, char b, char c, out char consumed)
        {
            SkipWhitespace();
            consumed = '\0';
            if (_position >= _text.Length)
                return false;
            var next = _text[_position];
            if (next != a && next != b && next != c)
                return false;
            consumed = next;
            _position++;
            return true;
        }

        private void SkipWhitespace()
        {
            while (_position < _text.Length && char.IsWhiteSpace(_text[_position]))
                _position++;
        }
    }
}
