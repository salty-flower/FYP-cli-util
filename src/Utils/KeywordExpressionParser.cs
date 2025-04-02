using System;
using System.Collections.Generic;

namespace DataCollection.Utils;

public class KeywordExpressionParser
{
    public static Func<Dictionary<string, int>, bool> ParseExpression(string expression)
    {
        expression = expression.Trim();

        // Create a parser instance to track position
        var parser = new ExpressionParser(expression);
        return parser.ParseExpression();
    }

    private class ExpressionParser(string expression)
    {
        private int _position;

        public Func<Dictionary<string, int>, bool> ParseExpression()
        {
            var result = ParseOr();

            // Ensure we've consumed the entire expression
            if (_position < expression.Length)
            {
                throw new ArgumentException(
                    $"Unexpected characters at position {_position}: {expression.Substring(_position)}"
                );
            }

            return result;
        }

        private Func<Dictionary<string, int>, bool> ParseOr()
        {
            var left = ParseAnd();

            while (MatchKeyword(" OR "))
            {
                var right = ParseAnd();
                var leftCopy = left; // Capture for lambda
                left = counts => leftCopy(counts) || right(counts);
            }

            return left;
        }

        private Func<Dictionary<string, int>, bool> ParseAnd()
        {
            var left = ParsePrimary();

            while (MatchKeyword(" AND "))
            {
                var right = ParsePrimary();
                var leftCopy = left; // Capture for lambda
                left = counts => leftCopy(counts) && right(counts);
            }

            return left;
        }

        private Func<Dictionary<string, int>, bool> ParsePrimary()
        {
            // Handle parenthesized expressions
            if (Match('('))
            {
                var expr = ParseOr();

                if (!Match(')'))
                {
                    throw new ArgumentException(
                        $"Expected closing parenthesis at position {_position}"
                    );
                }

                return expr;
            }

            // Handle simple expressions like "bug > 5"
            string simpleExpr = ParseSimpleExpression();
            if (TryParseSimpleExpression(simpleExpr, out var func))
            {
                return func!;
            }

            throw new ArgumentException(
                $"Invalid expression at position {_position}: {simpleExpr}"
            );
        }

        private string ParseSimpleExpression()
        {
            int start = _position;

            // Parse until we hit a parenthesis, AND, OR, or end of string
            while (_position < expression.Length)
            {
                if (expression[_position] == '(' || expression[_position] == ')')
                {
                    break;
                }

                if (
                    _position + 4 < expression.Length
                    && expression.Substring(_position, 5) == " AND "
                )
                {
                    break;
                }

                if (
                    _position + 3 < expression.Length
                    && expression.Substring(_position, 4) == " OR "
                )
                {
                    break;
                }

                _position++;
            }

            if (start == _position)
            {
                throw new ArgumentException($"Empty expression at position {_position}");
            }

            return expression.Substring(start, _position - start).Trim();
        }

        private bool Match(char expected)
        {
            if (_position >= expression.Length || expression[_position] != expected)
            {
                return false;
            }

            _position++;
            return true;
        }

        private bool MatchKeyword(string keyword)
        {
            if (_position + keyword.Length > expression.Length)
            {
                return false;
            }

            if (expression.Substring(_position, keyword.Length) != keyword)
            {
                return false;
            }

            _position += keyword.Length;
            return true;
        }

        private static bool TryParseSimpleExpression(
            string expression,
            out Func<Dictionary<string, int>, bool>? func
        )
        {
            func = null;

            // Match patterns like "keyword > 5", "keyword >= 10", etc.
            foreach (var op in new[] { ">=", "<=", ">", "<", "==" })
            {
                if (expression.Contains(op))
                {
                    var parts = expression.Split([op], StringSplitOptions.None);
                    if (parts.Length != 2)
                        return false;

                    var keyword = parts[0].Trim();
                    if (!int.TryParse(parts[1].Trim(), out var threshold))
                        return false;

                    func = counts =>
                    {
                        var count = counts.TryGetValue(keyword, out var c) ? c : 0;
                        return op switch
                        {
                            ">=" => count >= threshold,
                            "<=" => count <= threshold,
                            ">" => count > threshold,
                            "<" => count < threshold,
                            "==" => count == threshold,
                            _ => false,
                        };
                    };

                    return true;
                }
            }

            return false;
        }
    }
}
