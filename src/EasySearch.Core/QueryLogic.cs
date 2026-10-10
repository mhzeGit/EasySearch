using System.Text;

namespace EasySearch.Core;

/// <summary>
/// The boolean side of the search box: space or <c>AND</c> combines terms, <c>OR</c> accepts either,
/// <c>NOT</c> (or <c>!</c>) excludes and parentheses group, so
/// <c>report AND (ext:pdf OR size:&gt;1mb) NOT draft</c> reads the way it is written.
/// <para>
/// <c>OR</c> binds the two things next to it more tightly than <c>AND</c> or a space does
/// (<c>a OR b c</c> is "(a or b) and c"), the same as <c>a|b</c>; parentheses override that.
/// </para>
/// <para>
/// A query is turned into <i>branches</i> that are OR-ed together; each branch is a plain list of
/// AND-ed tokens in the syntax <see cref="QueryParser"/> already understands.
/// </para>
/// </summary>
static class QueryLogic
{
    /// <summary>A guard against (a OR b) (c OR d) ... multiplying out into thousands of branches.</summary>
    const int MaxBranches = 32;

    const string OrHint = "OR needs a word on both sides, e.g. report OR invoice";

    readonly record struct Token(string Text, bool Quoted, bool Paren);

    /// <summary>Raised for mismatched parentheses, which are then retried as ordinary characters.</summary>
    sealed class UnbalancedException(string message) : FormatException(message) { }

    /// <summary>
    /// The query as OR-ed branches of AND-ed tokens. An empty query is one empty branch (matches all).
    /// Lower-case <c>and</c>/<c>or</c> count as operators too, but only while the query still makes
    /// sense that way: "to be or not to be" falls back to plain words, and <c>NOT</c> must be capitals
    /// so "do not disturb" never silently drops a word.
    /// </summary>
    public static List<List<string>> Split(string text)
    {
        try { return Resolve(Tokenize(text, parens: true), lowerWords: true); }
        catch (FormatException) { }

        try { return Resolve(Tokenize(text, parens: true), lowerWords: false); }
        catch (UnbalancedException) { }

        // A ( or ) that never pairs up is just part of a name, as in "report (1".
        return Resolve(Tokenize(text, parens: false), lowerWords: false);
    }

    static List<List<string>> Resolve(List<Token> tokens, bool lowerWords)
    {
        var tree = new Parser(tokens, lowerWords).Parse();
        return Expand(tree).ConvertAll(branch => branch.ConvertAll(a => a.Negated ? "!" + a.Text : a.Text));
    }

    // ---- tokens -------------------------------------------------------------------------

    /// <summary>
    /// Splits on whitespace; double quotes keep spaces inside a token and are dropped (a quoted
    /// token is never an operator). A "(" opening a word and a ")" closing one become tokens of
    /// their own, so <c>file(1).txt</c> is left alone.
    /// </summary>
    static List<Token> Tokenize(string text, bool parens)
    {
        var tokens = new List<Token>();
        var current = new StringBuilder();
        bool inQuotes = false, hadQuotes = false;

        void Flush()
        {
            if (current.Length > 0) tokens.Add(new Token(current.ToString(), hadQuotes, false));
            current.Clear();
            hadQuotes = false;
        }

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"') { inQuotes = !inQuotes; hadQuotes = true; continue; }
            if (inQuotes) { current.Append(c); continue; }
            if (char.IsWhiteSpace(c)) { Flush(); continue; }

            if (parens && c == '(' && current.Length == 0)
            {
                tokens.Add(new Token("(", false, true));
                continue;
            }
            if (parens && c == ')' && (i + 1 == text.Length || char.IsWhiteSpace(text[i + 1]) || text[i + 1] == ')'))
            {
                Flush();
                tokens.Add(new Token(")", false, true));
                continue;
            }
            current.Append(c);
        }
        Flush();
        return tokens;
    }

    // ---- the expression tree ------------------------------------------------------------

    abstract class Node { }

    sealed class Atom(string text, bool negated) : Node
    {
        public readonly string Text = text;
        public readonly bool Negated = negated;
    }

    sealed class AllOf(List<Node> items) : Node { public readonly List<Node> Items = items; }

    sealed class AnyOf(List<Node> items) : Node { public readonly List<Node> Items = items; }

    sealed class Parser(List<Token> tokens, bool lowerWords)
    {
        int _i;

        bool More => _i < tokens.Count;
        Token Peek => tokens[_i];

        public Node Parse()
        {
            var node = Sequence();
            if (More) throw new UnbalancedException("There is a ) without a matching (");
            return node;
        }

        /// <summary>Terms joined by a space or AND, up to the end or a closing parenthesis.</summary>
        Node Sequence()
        {
            var items = new List<Node>();
            while (More && !IsClose(Peek))
            {
                if (IsAnd(Peek)) { _i++; continue; }
                items.Add(Alternatives());
            }
            return items.Count == 1 ? items[0] : new AllOf(items);
        }

        /// <summary>One thing, or several joined by OR.</summary>
        Node Alternatives()
        {
            var items = new List<Node> { Unary() };
            while (More && IsOr(Peek))
            {
                _i++;
                items.Add(Unary());
            }
            return items.Count == 1 ? items[0] : Merge(items);
        }

        Node Unary()
        {
            if (!More || IsClose(Peek) || IsAnd(Peek) || IsOr(Peek)) throw new FormatException(OrHint);

            var token = Peek;
            if (IsNot(token))
            {
                _i++;
                if (!More || IsClose(Peek) || IsAnd(Peek) || IsOr(Peek))
                    throw new FormatException("NOT needs a word after it, e.g. report NOT draft");
                return Negate(Unary());
            }

            if (token.Paren && token.Text == "(")
            {
                _i++;
                var inner = Sequence();
                if (!More) throw new UnbalancedException("There is a ( without a matching )");
                _i++; // the closing parenthesis

                // "(1)" around one plain word is part of a name such as "report (1).pdf", not a group.
                if (inner is Atom { Negated: false } word && KeyOf(word.Text) is null && !word.Text.Contains('|'))
                    return new Atom("(" + word.Text + ")", false);
                return inner;
            }

            _i++;
            return token.Text.Length > 1 && token.Text[0] == '!' ? new Atom(token.Text[1..], true) : new Atom(token.Text, false);
        }

        bool IsClose(Token t) => t.Paren && t.Text == ")";

        bool IsAnd(Token t) => !t.Quoted && !t.Paren
            && (t.Text == "AND" || t.Text == "&&" || (lowerWords && t.Text.Equals("and", StringComparison.OrdinalIgnoreCase)));

        bool IsOr(Token t) => !t.Quoted && !t.Paren
            && (t.Text == "OR" || t.Text == "||" || t.Text == "|" || (lowerWords && t.Text.Equals("or", StringComparison.OrdinalIgnoreCase)));

        bool IsNot(Token t) => !t.Quoted && !t.Paren && t.Text == "NOT";
    }

    // ---- helpers ------------------------------------------------------------------------

    static readonly HashSet<string> OperatorKeys = new(StringComparer.OrdinalIgnoreCase)
        { "ext", "type", "kind", "folder", "folders", "file", "files", "size", "date", "dm", "modified", "datemodified", "changed", "updated",
          "in", "source", "location", "inside", "where", "under", "parent", "path", "name", "regex" };

    /// <summary>Operators whose values are lists, so <c>a OR b</c> can become <c>key:a|b</c>.</summary>
    static readonly HashSet<string> JoinableKeys = new(StringComparer.OrdinalIgnoreCase) { "ext", "type", "kind", "path", "name" };

    /// <summary>The operator a token starts with ("ext" for "ext:pdf"), or null for plain text.</summary>
    static string? KeyOf(string token)
    {
        int colon = token.IndexOf(':');
        return colon > 0 && OperatorKeys.Contains(token[..colon]) ? token[..colon].ToLowerInvariant() : null;
    }

    /// <summary>
    /// <c>a OR b</c> between plain words, or between two <c>ext:</c>/<c>type:</c>/... filters of the same
    /// kind, is one term with alternatives (<c>a|b</c>, <c>ext:pdf|docx</c>), which is also the fastest
    /// way to run it. Anything else stays a real OR between branches.
    /// </summary>
    static Node Merge(List<Node> items)
    {
        if (items.All(i => i is Atom { Negated: false }))
        {
            var atoms = items.Cast<Atom>().ToList();
            var keys = atoms.Select(a => KeyOf(a.Text)).Distinct().ToList();
            if (keys is [null])
                return new Atom(string.Join('|', atoms.Select(a => a.Text)), false);
            if (keys is [{ } key] && JoinableKeys.Contains(key))
                return new Atom(key + ":" + string.Join('|', atoms.Select(a => a.Text[(a.Text.IndexOf(':') + 1)..])), false);
        }
        return new AnyOf(items);
    }

    /// <summary>NOT pushed down to the words: NOT (a OR b) is !a AND !b, NOT (a AND b) is !a OR !b.</summary>
    static Node Negate(Node node) => node switch
    {
        Atom a => new Atom(a.Text, !a.Negated),
        AllOf all => new AnyOf(all.Items.ConvertAll(Negate)),
        AnyOf any => new AllOf(any.Items.ConvertAll(Negate)),
        _ => node,
    };

    /// <summary>Multiplies the tree out into OR-ed branches of AND-ed words.</summary>
    static List<List<Atom>> Expand(Node node)
    {
        switch (node)
        {
            case Atom atom:
                return [[atom]];

            case AnyOf any:
                var union = new List<List<Atom>>();
                foreach (var item in any.Items) union.AddRange(Expand(item));
                return Check(union);

            case AllOf all:
                var product = new List<List<Atom>> { new() };
                foreach (var item in all.Items)
                {
                    var options = Expand(item);
                    var next = new List<List<Atom>>(product.Count * options.Count);
                    foreach (var left in product)
                        foreach (var right in options)
                            next.Add([.. left, .. right]);
                    product = Check(next);
                }
                return product;

            default:
                return [[]];
        }
    }

    static List<List<Atom>> Check(List<List<Atom>> branches) =>
        branches.Count <= MaxBranches
            ? branches
            : throw new FormatException($"That search splits into more than {MaxBranches} OR branches. Try fewer ORs or parentheses");
}
