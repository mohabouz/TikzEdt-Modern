using System.Globalization;

namespace TikzEdt.Modern.Core.Documents;

/// <summary>A bounded, non-executing subset of PGF math for overlay coordinates.</summary>
public static class PgfConstantExpression
{
    public static bool TryEvaluate(string expression, out double value)
    {
        value = 0;
        if (expression.Length is 0 or > 1024 || expression.Contains('\\') || expression.Contains('$')) return false;
        try { var parser = new Reader(expression); value = parser.Read(); return double.IsFinite(value) && Math.Abs(value) < 1e12; }
        catch (FormatException) { return false; }
    }
    private sealed class Reader(string text)
    {
        private int _at, _depth;
        private void Space() { while (_at < text.Length && char.IsWhiteSpace(text[_at])) _at++; }
        private bool Take(char c) { Space(); if (_at >= text.Length || text[_at] != c) return false; _at++; return true; }
        public double Read() { var n = Sum(); Space(); if (_at != text.Length) throw new FormatException(); return n; }
        private double Sum() { var n = Product(); while (true) { if (Take('+')) n += Product(); else if (Take('-')) n -= Product(); else return n; } }
        private double Product() { var n = Unary(); while (true) { if (Take('*')) n *= Unary(); else if (Take('/')) n /= Unary(); else return n; } }
        private double Unary()
        {
            if (++_depth > 48) throw new FormatException();
            try { if (Take('+')) return Unary(); if (Take('-')) return -Unary(); var n = Primary(); return Take('^') ? Math.Pow(n, Unary()) : n; }
            finally { _depth--; }
        }
        private double Primary()
        {
            if (Take('(')) { var n = Sum(); if (!Take(')')) throw new FormatException(); return n; }
            if (Take('{')) { var n = Sum(); if (!Take('}')) throw new FormatException(); return n; }
            Space(); var start = _at;
            if (_at < text.Length && char.IsLetter(text[_at]))
            {
                while (_at < text.Length && char.IsLetterOrDigit(text[_at])) _at++;
                var name = text[start.._at]; if (name == "pi") return Math.PI; if (name == "e") return Math.E;
                if (!Take('(')) throw new FormatException(); var a = Sum(); double? b = Take(',') ? Sum() : null; if (!Take(')')) throw new FormatException();
                if (b is { } second) return name switch { "min" => Math.Min(a, second), "max" => Math.Max(a, second), "pow" => Math.Pow(a, second), _ => throw new FormatException() };
                return name switch { "exp" => Math.Exp(a), "ln" => Math.Log(a), "sqrt" => Math.Sqrt(a), "abs" => Math.Abs(a), "sin" => Math.Sin(a * Math.PI / 180), "cos" => Math.Cos(a * Math.PI / 180), "tan" => Math.Tan(a * Math.PI / 180), "asin" => Math.Asin(a) * 180 / Math.PI, "acos" => Math.Acos(a) * 180 / Math.PI, "atan" => Math.Atan(a) * 180 / Math.PI, "floor" => Math.Floor(a), "ceil" => Math.Ceiling(a), "round" => Math.Round(a, MidpointRounding.AwayFromZero), "deg" => a * 180 / Math.PI, "rad" => a * Math.PI / 180, _ => throw new FormatException() };
            }
            while (_at < text.Length && (char.IsDigit(text[_at]) || text[_at] == '.')) _at++;
            if (_at < text.Length && text[_at] is 'e' or 'E') { _at++; if (_at < text.Length && text[_at] is '+' or '-') _at++; while (_at < text.Length && char.IsDigit(text[_at])) _at++; }
            if (!double.TryParse(text[start.._at], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) throw new FormatException();
            return value;
        }
    }
}
