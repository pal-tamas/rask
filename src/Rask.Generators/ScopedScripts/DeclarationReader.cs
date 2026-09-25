using System;
using System.Collections.Generic;
using System.Text;

namespace Rask.Generators.ScopedScripts;

/// <summary>
///     Reads the declaration file tsgo writes beside a component's compiled scoped script — the exported
///     functions, classes and data shapes the generator turns into typed private members.
/// </summary>
/// <remarks>
///     <para>
///         Not a TypeScript parser. The input is tsgo's own <c>--declaration</c> output, which is already
///         normalized: every export is a <c>declare</c> with an explicit type, bodies are gone, and inferred
///         types are written out. That narrow, regular form is what makes a small recursive descent enough.
///     </para>
///     <para>
///         Anything it does not understand becomes <see cref="TsUnsupported" /> rather than an exception, so
///         the export that uses it is reported (RASK094) and every other export still gets its method.
///     </para>
/// </remarks>
internal static class DeclarationReader
{
    public static TsDeclarations Read(string text)
    {
        var parser = new Parser(Tokenize(text));
        return parser.ReadAll();
    }

    private static List<Token> Tokenize(string s)
    {
        var tokens = new List<Token>();
        string? doc = null;
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
            {
                while (i < s.Length && s[i] != '\n')
                {
                    i++;
                }

                continue;
            }

            if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                var end = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? s.Length : end;
                if (i + 2 < s.Length && s[i + 2] == '*')
                {
                    doc = s.Substring(i + 3, Math.Max(0, end - (i + 3)));
                }

                i = Math.Min(s.Length, end + 2);
                continue;
            }

            string value;
            var kind = TokenKind.Punct;
            if (char.IsLetter(c) || c == '_' || c == '$' || c == '#')
            {
                var start = i;
                i++;
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_' || s[i] == '$'))
                {
                    i++;
                }

                value = s.Substring(start, i - start);
                kind = TokenKind.Name;
            }
            else if (char.IsDigit(c) || (c == '-' && i + 1 < s.Length && char.IsDigit(s[i + 1])))
            {
                var start = i;
                i++;
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '.' || s[i] == '_'))
                {
                    i++;
                }

                value = s.Substring(start, i - start);
                kind = TokenKind.Number;
            }
            else if (c == '"' || c == '\'' || c == '`')
            {
                var start = i;
                i++;
                while (i < s.Length && s[i] != c)
                {
                    i += s[i] == '\\' ? 2 : 1;
                }

                i = Math.Min(s.Length, i + 1);
                value = s.Substring(start, i - start);
                kind = TokenKind.String;
            }
            else if (c == '.' && i + 2 < s.Length && s[i + 1] == '.' && s[i + 2] == '.')
            {
                value = "...";
                i += 3;
            }
            else if (c == '=' && i + 1 < s.Length && s[i + 1] == '>')
            {
                value = "=>";
                i += 2;
            }
            else
            {
                value = c.ToString();
                i++;
            }

            tokens.Add(new Token(kind, value, doc));
            doc = null;
        }

        tokens.Add(new Token(TokenKind.End, string.Empty, null));
        return tokens;
    }

    private sealed class Parser(List<Token> tokens)
    {
        private int _pos;

        private Token Current => tokens[_pos];

        private bool At(string value) => Current.Kind != TokenKind.String && Current.Value == value;

        private bool AtEnd => Current.Kind == TokenKind.End;

        private Token Next()
        {
            var t = tokens[_pos];
            if (_pos < tokens.Count - 1)
            {
                _pos++;
            }

            return t;
        }

        private bool Accept(string value)
        {
            if (!At(value))
            {
                return false;
            }

            Next();
            return true;
        }

        private Token Peek(int ahead) => tokens[Math.Min(_pos + ahead, tokens.Count - 1)];

        public TsDeclarations ReadAll()
        {
            var result = new TsDeclarations();
            while (!AtEnd)
            {
                var start = _pos;
                ReadStatement(result);
                if (_pos == start)
                {
                    Next();
                }
            }

            return result;
        }

        private void ReadStatement(TsDeclarations result)
        {
            var doc = Current.Doc;
            var exported = Accept("export");
            Accept("declare");
            if (exported && At("default"))
            {
                Next();
            }

            Accept("async");
            var isAbstract = Accept("abstract");

            if (At("function"))
            {
                Next();
                var fn = ReadFunction(doc);
                if (fn is null)
                {
                    SkipStatement();
                    return;
                }

                if (exported)
                {
                    result.Functions.Add(fn);
                }

                return;
            }

            if (At("class"))
            {
                Next();
                var cls = ReadClass(doc, isAbstract);
                if (cls is not null && exported)
                {
                    result.Classes.Add(cls);
                }

                return;
            }

            if (At("interface"))
            {
                Next();
                var name = Next().Value;
                var generic = At("<");
                SkipTypeParameters();

                // `interface A extends B` copies nothing we can see without resolving B; kept as unsupported
                // rather than half a shape.
                var extends = false;
                while (!At("{") && !AtEnd)
                {
                    extends = true;
                    Next();
                }

                var body = ReadObjectBody();
                result.Aliases[name] = generic || extends
                    ? new TsUnsupported(generic ? $"the generic interface '{name}'" : $"'{name}', which extends another type")
                    : body;
                return;
            }

            if (At("type") && Peek(1).Kind == TokenKind.Name)
            {
                Next();
                var name = Next().Value;
                var generic = At("<");
                SkipTypeParameters();
                if (!Accept("="))
                {
                    SkipStatement();
                    return;
                }

                var type = ReadType();
                Accept(";");
                result.Aliases[name] = generic ? new TsUnsupported($"the generic type '{name}'") : type;
                return;
            }

            if (exported && (At("const") || At("let") || At("var")))
            {
                Next();
                result.NotCallable.Add(Current.Value);
                SkipStatement();
                return;
            }

            SkipStatement();
        }

        private TsFunctionDecl? ReadFunction(string? doc)
        {
            if (Current.Kind != TokenKind.Name)
            {
                return null;
            }

            var name = Next().Value;
            var generic = At("<");
            SkipTypeParameters();
            if (!At("("))
            {
                return null;
            }

            var parameters = ReadParameters();
            var returns = Accept(":") ? ReadReturnType() : new TsNamed("void");
            Accept(";");
            return new TsFunctionDecl(name, parameters, returns, generic, doc);
        }

        private TsClassDecl? ReadClass(string? doc, bool isAbstract)
        {
            if (Current.Kind != TokenKind.Name)
            {
                SkipStatement();
                return null;
            }

            var name = Next().Value;
            var generic = At("<");
            SkipTypeParameters();

            // `extends Base` / `implements I` — the inherited members are not in this declaration, so a proxy
            // would silently miss them; the class is kept, its own members only.
            while (!At("{") && !AtEnd)
            {
                Next();
            }

            if (!Accept("{"))
            {
                return null;
            }

            var cls = new TsClassDecl(name, doc, generic, isAbstract);
            var constructible = true;
            while (!At("}") && !AtEnd)
            {
                var start = _pos;
                var memberDoc = Current.Doc;
                var hidden = false;
                var isStatic = false;
                while (At("private") || At("protected") || At("public") || At("static") || At("readonly")
                       || At("abstract") || At("declare") || At("override") || At("accessor") || At("async"))
                {
                    hidden |= At("private") || At("protected");
                    isStatic |= At("static");
                    Next();
                }

                if (At("constructor"))
                {
                    Next();
                    var parameters = ReadParameters();
                    Accept(";");
                    if (hidden)
                    {
                        constructible = false;
                    }
                    else
                    {
                        cls.Constructors.Add(parameters);
                    }
                }
                else if ((At("get") || At("set")) && Peek(1).Kind == TokenKind.Name && Peek(2).Value != "(")
                {
                    SkipMember();
                }
                else if (Current.Kind == TokenKind.Name && !Current.Value.StartsWith("#", StringComparison.Ordinal)
                         && (Peek(1).Value == "(" || Peek(1).Value == "<"
                             || (Peek(1).Value == "?" && Peek(2).Value == "(")))
                {
                    var methodName = Next().Value;
                    Accept("?");
                    var genericMethod = At("<");
                    SkipTypeParameters();
                    var parameters = ReadParameters();
                    var returns = Accept(":") ? ReadReturnType() : new TsNamed("void");
                    Accept(";");
                    if (!hidden && !isStatic)
                    {
                        cls.Methods.Add(new TsFunctionDecl(methodName, parameters, returns, genericMethod, memberDoc));
                    }
                }
                else
                {
                    SkipMember();
                }

                if (_pos == start)
                {
                    Next();
                }
            }

            Accept("}");
            if (constructible && !isAbstract && cls.Constructors.Count == 0)
            {
                cls.Constructors.Add(new List<TsParam>());
            }

            if (!constructible)
            {
                cls.Constructors.Clear();
            }

            return cls;
        }

        private List<TsParam> ReadParameters()
        {
            var parameters = new List<TsParam>();
            if (!Accept("("))
            {
                return parameters;
            }

            var index = 0;
            while (!At(")") && !AtEnd)
            {
                var start = _pos;
                while (At("public") || At("private") || At("protected") || At("readonly"))
                {
                    Next();
                }

                var rest = Accept("...");
                string name;
                if (At("{") || At("["))
                {
                    SkipBalanced();
                    name = "arg" + index;
                }
                else
                {
                    name = Next().Value;
                }

                var optional = Accept("?");
                var type = Accept(":") ? ReadType() : new TsNamed("any");
                if (Accept("="))
                {
                    optional = true;
                    SkipUntilParameterEnd();
                }

                if (name != "this")
                {
                    parameters.Add(new TsParam(name, type, optional, rest));
                }

                index++;
                Accept(",");
                if (_pos == start)
                {
                    Next();
                }
            }

            Accept(")");
            return parameters;
        }

        // A return type may be a predicate (`x is Foo`), which is a boolean at run time.
        private TsType ReadReturnType()
        {
            if (Accept("asserts"))
            {
                SkipUntilParameterEnd();
                return new TsUnsupported("an assertion signature");
            }

            if (Current.Kind == TokenKind.Name && Peek(1).Value == "is")
            {
                Next();
                Next();
                ReadType();
                return new TsNamed("boolean");
            }

            return ReadType();
        }

        public TsType ReadType()
        {
            Accept("|");
            var first = ReadIntersection();
            if (!At("|"))
            {
                return first;
            }

            var members = new List<TsType> { first };
            while (Accept("|"))
            {
                members.Add(ReadIntersection());
            }

            return new TsUnion(members);
        }

        private TsType ReadIntersection()
        {
            Accept("&");
            var first = ReadPostfix();
            if (!At("&"))
            {
                return first;
            }

            while (Accept("&"))
            {
                ReadPostfix();
            }

            return new TsUnsupported("an intersection type");
        }

        private TsType ReadPostfix()
        {
            var type = ReadAtom();
            while (At("[") && Peek(1).Value == "]")
            {
                Next();
                Next();
                type = new TsArray(type);
            }

            if (At("[") || At("extends"))
            {
                SkipUntilParameterEnd();
                return new TsUnsupported("an indexed or conditional type");
            }

            return type;
        }

        private TsType ReadAtom()
        {
            if (Accept("readonly"))
            {
                return ReadPostfix();
            }

            if (At("keyof") || At("typeof") || At("unique") || At("infer"))
            {
                var word = Next().Value;
                ReadPostfix();
                return new TsUnsupported($"a '{word}' type");
            }

            if (Current.Kind == TokenKind.String)
            {
                Next();
                return new TsLiteral(TsLiteralKind.String);
            }

            if (Current.Kind == TokenKind.Number)
            {
                Next();
                return new TsLiteral(TsLiteralKind.Number);
            }

            if (At("true") || At("false"))
            {
                Next();
                return new TsLiteral(TsLiteralKind.Boolean);
            }

            if (At("new"))
            {
                Next();
                ReadFunctionType();
                return new TsUnsupported("a constructor type");
            }

            if (At("<"))
            {
                SkipTypeParameters();
                var fn = ReadFunctionType();
                return fn is TsFunction f ? new TsFunction(f.Parameters, f.Returns, true) : fn;
            }

            if (At("("))
            {
                return LooksLikeFunctionType() ? ReadFunctionType() : ReadParenthesized();
            }

            if (At("{"))
            {
                var body = ReadObjectBody();
                return new TsInlineObject(body);
            }

            if (At("["))
            {
                SkipBalanced();
                return new TsUnsupported("a tuple");
            }

            if (Current.Kind == TokenKind.Name)
            {
                var name = Next().Value;
                while (At(".") && Peek(1).Kind == TokenKind.Name)
                {
                    Next();
                    name += "." + Next().Value;
                }

                var args = new List<TsType>();
                if (Accept("<"))
                {
                    while (!At(">") && !AtEnd)
                    {
                        var start = _pos;
                        args.Add(ReadType());
                        Accept(",");
                        if (_pos == start)
                        {
                            Next();
                        }
                    }

                    Accept(">");
                }

                return new TsNamed(name, args);
            }

            var unknown = Next().Value;
            return new TsUnsupported($"'{unknown}'");
        }

        private bool LooksLikeFunctionType()
        {
            // `()`, `(a:`, `(a?`, `(a,`, `(a)` followed by `=>`, `(...`, `({`, `([` — a parameter list.
            var p1 = Peek(1);
            if (p1.Value is ")" or "...")
            {
                return true;
            }

            if (p1.Value is "{" or "[")
            {
                return true;
            }

            var p2 = Peek(2);
            if (p1.Kind == TokenKind.Name && p2.Value is ":" or "?" or ",")
            {
                return true;
            }

            return p1.Kind == TokenKind.Name && p2.Value == ")" && Peek(3).Value == "=>";
        }

        private TsType ReadParenthesized()
        {
            Next();
            var inner = ReadType();
            Accept(")");
            return inner;
        }

        private TsType ReadFunctionType()
        {
            var parameters = ReadParameters();
            if (!Accept("=>"))
            {
                return new TsUnsupported("a function type");
            }

            var returns = ReadReturnType();
            return new TsFunction(parameters, returns, false);
        }

        private TsObject ReadObjectBody()
        {
            var obj = new TsObject();
            if (!Accept("{"))
            {
                return obj;
            }

            while (!At("}") && !AtEnd)
            {
                var start = _pos;
                Accept("readonly");
                if (At("["))
                {
                    SkipMember();
                }
                else if (Current.Kind is TokenKind.Name or TokenKind.String
                         && (Peek(1).Value is ":" || (Peek(1).Value == "?" && Peek(2).Value == ":")))
                {
                    var raw = Next();
                    var name = raw.Kind == TokenKind.String ? raw.Value.Substring(1, raw.Value.Length - 2) : raw.Value;
                    var optional = Accept("?");
                    Accept(":");
                    obj.Properties.Add(new TsProperty(name, ReadType(), optional));
                }
                else
                {
                    // A method, call or construct signature: not data, and JSON carries no functions.
                    SkipMember();
                }

                if (!Accept(";"))
                {
                    Accept(",");
                }

                if (_pos == start)
                {
                    Next();
                }
            }

            Accept("}");
            Accept(";");
            return obj;
        }

        private void SkipTypeParameters()
        {
            if (!At("<"))
            {
                return;
            }

            var depth = 0;
            do
            {
                if (At("<"))
                {
                    depth++;
                }
                else if (At(">"))
                {
                    depth--;
                }

                Next();
            }
            while (depth > 0 && !AtEnd);
        }

        private void SkipBalanced()
        {
            var depth = 0;
            do
            {
                if (At("{") || At("(") || At("["))
                {
                    depth++;
                }
                else if (At("}") || At(")") || At("]"))
                {
                    depth--;
                }

                Next();
            }
            while (depth > 0 && !AtEnd);
        }

        // To the `,` or `)` that ends a parameter, or the `;` that ends a member, at this depth.
        private void SkipUntilParameterEnd()
        {
            while (!AtEnd && !At(",") && !At(")") && !At(";") && !At("}") && !At(">"))
            {
                if (At("{") || At("(") || At("["))
                {
                    SkipBalanced();
                }
                else
                {
                    Next();
                }
            }
        }

        private void SkipMember()
        {
            while (!AtEnd && !At(";") && !At("}"))
            {
                if (At("{") || At("(") || At("["))
                {
                    SkipBalanced();
                }
                else
                {
                    Next();
                }
            }

            Accept(";");
        }

        private void SkipStatement()
        {
            while (!AtEnd && !At(";"))
            {
                if (At("{"))
                {
                    SkipBalanced();
                    Accept(";");
                    return;
                }

                if (At("(") || At("["))
                {
                    SkipBalanced();
                }
                else
                {
                    Next();
                }
            }

            Accept(";");
        }
    }

    private enum TokenKind
    {
        Name,
        Number,
        String,
        Punct,
        End,
    }

    private readonly struct Token(TokenKind kind, string value, string? doc)
    {
        public TokenKind Kind { get; } = kind;
        public string Value { get; } = value;
        public string? Doc { get; } = doc;
    }
}

/// <summary>Everything a component's declaration file exports that the generator can act on.</summary>
internal sealed class TsDeclarations
{
    public List<TsFunctionDecl> Functions { get; } = new();
    public List<TsClassDecl> Classes { get; } = new();

    /// <summary>Interfaces and type aliases, exported or not — what a named type in a signature resolves to.</summary>
    public Dictionary<string, TsType> Aliases { get; } = new(StringComparer.Ordinal);

    /// <summary><c>export const f = …</c> — exported, but the runtime only exposes functions and classes.</summary>
    public List<string> NotCallable { get; } = new();
}

internal sealed class TsFunctionDecl(string name, List<TsParam> parameters, TsType returns, bool generic, string? doc)
{
    public string Name { get; } = name;
    public List<TsParam> Parameters { get; } = parameters;
    public TsType Returns { get; } = returns;
    public bool Generic { get; } = generic;
    public string? Doc { get; } = doc;
}

internal sealed class TsClassDecl(string name, string? doc, bool generic, bool isAbstract)
{
    public string Name { get; } = name;
    public string? Doc { get; } = doc;
    public bool Generic { get; } = generic;
    public bool IsAbstract { get; } = isAbstract;
    public List<List<TsParam>> Constructors { get; } = new();
    public List<TsFunctionDecl> Methods { get; } = new();
}

internal sealed class TsParam(string name, TsType type, bool optional, bool rest)
{
    public string Name { get; } = name;
    public TsType Type { get; } = type;
    public bool Optional { get; } = optional;
    public bool Rest { get; } = rest;
}

internal sealed class TsProperty(string name, TsType type, bool optional)
{
    public string Name { get; } = name;
    public TsType Type { get; } = type;
    public bool Optional { get; } = optional;
}

internal abstract class TsType;

internal sealed class TsNamed(string name, List<TsType>? args = null) : TsType
{
    public string Name { get; } = name;
    public List<TsType> Args { get; } = args ?? new List<TsType>();
}

internal sealed class TsArray(TsType element) : TsType
{
    public TsType Element { get; } = element;
}

internal sealed class TsUnion(List<TsType> members) : TsType
{
    public List<TsType> Members { get; } = members;
}

internal enum TsLiteralKind
{
    String,
    Number,
    Boolean,
}

internal sealed class TsLiteral(TsLiteralKind kind) : TsType
{
    public TsLiteralKind Kind { get; } = kind;
}

internal sealed class TsFunction(List<TsParam> parameters, TsType returns, bool generic) : TsType
{
    public List<TsParam> Parameters { get; } = parameters;
    public TsType Returns { get; } = returns;
    public bool Generic { get; } = generic;
}

/// <summary>An object shape — an interface body or a <c>type X = { … }</c>.</summary>
internal sealed class TsObject : TsType
{
    public List<TsProperty> Properties { get; } = new();
}

/// <summary>An object shape written inline in a signature, which has no name to give its record.</summary>
internal sealed class TsInlineObject(TsObject shape) : TsType
{
    public TsObject Shape { get; } = shape;
}

internal sealed class TsUnsupported(string description) : TsType
{
    public string Description { get; } = description;
}

internal static class DocComment
{
    /// <summary>The JSDoc text of a declaration, split into its summary, <c>@param</c>s and <c>@returns</c>.</summary>
    public static (string Summary, Dictionary<string, string> Params, string? Returns) Split(string? doc)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(doc))
        {
            return (string.Empty, parameters, null);
        }

        var summary = new StringBuilder();
        string? returns = null;
        StringBuilder? current = summary;
        string? currentParam = null;
        var inReturns = false;

        void Flush()
        {
            if (currentParam is not null && current is not null)
            {
                parameters[currentParam] = current.ToString().Trim();
            }
            else if (inReturns && current is not null)
            {
                returns = current.ToString().Trim();
            }
        }

        foreach (var rawLine in doc!.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("*", StringComparison.Ordinal))
            {
                line = line.Substring(1).Trim();
            }

            if (line.StartsWith("@", StringComparison.Ordinal))
            {
                Flush();
                currentParam = null;
                inReturns = false;
                current = null;
                if (line.StartsWith("@param ", StringComparison.Ordinal))
                {
                    var restOfLine = line.Substring("@param ".Length).Trim();
                    if (restOfLine.StartsWith("{", StringComparison.Ordinal))
                    {
                        var close = restOfLine.IndexOf('}');
                        restOfLine = close < 0 ? string.Empty : restOfLine.Substring(close + 1).Trim();
                    }

                    var space = restOfLine.IndexOf(' ');
                    currentParam = space < 0 ? restOfLine : restOfLine.Substring(0, space);
                    current = new StringBuilder(space < 0 ? string.Empty : restOfLine.Substring(space + 1).TrimStart('-', ' '));
                }
                else if (line.StartsWith("@returns", StringComparison.Ordinal) || line.StartsWith("@return ", StringComparison.Ordinal))
                {
                    inReturns = true;
                    var space = line.IndexOf(' ');
                    current = new StringBuilder(space < 0 ? string.Empty : line.Substring(space + 1));
                }

                continue;
            }

            if (current is null)
            {
                continue;
            }

            if (current.Length > 0 && line.Length > 0)
            {
                current.Append(' ');
            }

            current.Append(line);
        }

        Flush();
        return (summary.ToString().Trim(), parameters, returns);
    }
}
