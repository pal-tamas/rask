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
            if (SkipTrivia(s, ref i, ref doc))
            {
                continue;
            }

            var (kind, value) = ReadToken(s, ref i);
            tokens.Add(new Token(kind, value, doc));
            doc = null;
        }

        tokens.Add(new Token(TokenKind.End, string.Empty, null));
        return tokens;
    }

    // Whitespace and comments. A `/** … */` block is kept as the doc of the token after it.
    private static bool SkipTrivia(string s, ref int i, ref string? doc)
    {
        var c = s[i];
        if (char.IsWhiteSpace(c))
        {
            i++;
            return true;
        }

        if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
        {
            while (i < s.Length && s[i] != '\n')
            {
                i++;
            }

            return true;
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
            return true;
        }

        return false;
    }

    private static (TokenKind Kind, string Value) ReadToken(string s, ref int i)
    {
        var c = s[i];
        var start = i;
        if (char.IsLetter(c) || c == '_' || c == '$' || c == '#')
        {
            i++;
            while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_' || s[i] == '$'))
            {
                i++;
            }

            return (TokenKind.Name, s.Substring(start, i - start));
        }

        if (char.IsDigit(c) || (c == '-' && i + 1 < s.Length && char.IsDigit(s[i + 1])))
        {
            i++;
            while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '.' || s[i] == '_'))
            {
                i++;
            }

            return (TokenKind.Number, s.Substring(start, i - start));
        }

        if (c == '"' || c == '\'' || c == '`')
        {
            i++;
            while (i < s.Length && s[i] != c)
            {
                i += s[i] == '\\' ? 2 : 1;
            }

            i = Math.Min(s.Length, i + 1);
            return (TokenKind.String, s.Substring(start, i - start));
        }

        if (c == '.' && i + 2 < s.Length && s[i + 1] == '.' && s[i + 2] == '.')
        {
            i += 3;
            return (TokenKind.Punct, "...");
        }

        if (c == '=' && i + 1 < s.Length && s[i + 1] == '>')
        {
            i += 2;
            return (TokenKind.Punct, "=>");
        }

        i++;
        return (TokenKind.Punct, c.ToString());
    }

    private sealed class Parser(List<Token> tokens)
    {
        private int _pos;

        private Token Current => tokens[_pos];

        private bool At(string value) =>
            Current.Kind != TokenKind.String && string.Equals(Current.Value, value, StringComparison.Ordinal);

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

            if (Accept("function"))
            {
                ReadFunctionStatement(result, doc, exported);
            }
            else if (Accept("class"))
            {
                var cls = ReadClass(doc, isAbstract);
                if (cls is not null && exported)
                {
                    result.Classes.Add(cls);
                }
            }
            else if (Accept("interface"))
            {
                ReadInterface(result);
            }
            else if (At("type") && Peek(1).Kind == TokenKind.Name)
            {
                Next();
                ReadTypeAlias(result);
            }
            else if (exported && (At("const") || At("let") || At("var")))
            {
                Next();
                ReadExportedVariable(result, doc);
            }
            else
            {
                SkipStatement();
            }
        }

        private void ReadFunctionStatement(TsDeclarations result, string? doc, bool exported)
        {
            var fn = ReadFunction(doc);
            if (fn is null)
            {
                SkipStatement();
            }
            else if (exported)
            {
                result.Functions.Add(fn);
            }
        }

        private void ReadInterface(TsDeclarations result)
        {
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
            if (generic)
            {
                result.Aliases[name] = new TsUnsupported($"the generic interface '{name}'");
            }
            else if (extends)
            {
                result.Aliases[name] = new TsUnsupported($"'{name}', which extends another type");
            }
            else
            {
                result.Aliases[name] = body;
            }
        }

        private void ReadTypeAlias(TsDeclarations result)
        {
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
        }

        private void ReadExportedVariable(TsDeclarations result, string? doc)
        {
            // `export const double = (x: number) => x * 2` is declared `declare const double: (x: number) =>
            // number` — a function in all but keyword, so it gets a method like one. A value (`= 3.14`) has
            // nothing to call.
            var name = Current.Value;
            Next();
            if (Accept(":") && ReadType() is TsFunction fn)
            {
                result.Functions.Add(new TsFunctionDecl(name, fn.Parameters, fn.Returns, fn.Generic, doc));
                Accept(";");
                return;
            }

            result.NotCallable.Add(name);
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
                constructible &= ReadClassMember(cls);
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

        // One member of a class body. False for a constructor the class hides, which leaves it unconstructible.
        private bool ReadClassMember(TsClassDecl cls)
        {
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

            if (Accept("constructor"))
            {
                var parameters = ReadParameters();
                Accept(";");
                if (hidden)
                {
                    return false;
                }

                cls.Constructors.Add(parameters);
            }
            else if ((At("get") || At("set")) && Peek(1).Kind == TokenKind.Name && Peek(2).Value is not "(")
            {
                SkipMember();
            }
            else if (Current.Kind == TokenKind.Name && !Current.Value.StartsWith("#", StringComparison.Ordinal)
                     && (Peek(1).Value is "(" or "<" || (Peek(1).Value is "?" && Peek(2).Value is "(")))
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

            return true;
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

                if (name is not "this")
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
        private ITsType ReadReturnType()
        {
            if (Accept("asserts"))
            {
                SkipUntilParameterEnd();
                return new TsUnsupported("an assertion signature");
            }

            if (Current.Kind == TokenKind.Name && Peek(1).Value is "is")
            {
                Next();
                Next();
                ReadType();
                return new TsNamed("boolean");
            }

            return ReadType();
        }

        public ITsType ReadType()
        {
            Accept("|");
            var first = ReadIntersection();
            if (!At("|"))
            {
                return first;
            }

            var members = new List<ITsType> { first };
            while (Accept("|"))
            {
                members.Add(ReadIntersection());
            }

            return new TsUnion(members);
        }

        private ITsType ReadIntersection()
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

        private ITsType ReadPostfix()
        {
            var type = ReadAtom();
            while (At("[") && Peek(1).Value is "]")
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

        private ITsType ReadAtom()
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

            if (ReadLiteral() is { } literal)
            {
                return literal;
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
                return new TsInlineObject(ReadObjectBody());
            }

            if (At("["))
            {
                return ReadTuple();
            }

            if (Current.Kind == TokenKind.Name)
            {
                return ReadNamed();
            }

            var unknown = Next().Value;
            return new TsUnsupported($"'{unknown}'");
        }

        private TsLiteral? ReadLiteral()
        {
            TsLiteralKind kind;
            if (Current.Kind == TokenKind.String)
            {
                kind = TsLiteralKind.String;
            }
            else if (Current.Kind == TokenKind.Number)
            {
                kind = TsLiteralKind.Number;
            }
            else if (At("true") || At("false"))
            {
                kind = TsLiteralKind.Boolean;
            }
            else
            {
                return null;
            }

            Next();
            return new TsLiteral(kind);
        }

        // A named type, qualified or not, with its type arguments.
        private TsNamed ReadNamed()
        {
            var name = new StringBuilder(Next().Value);
            while (At(".") && Peek(1).Kind == TokenKind.Name)
            {
                Next();
                name.Append('.').Append(Next().Value);
            }

            var args = new List<ITsType>();
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

            return new TsNamed(name.ToString(), args);
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

            return p1.Kind == TokenKind.Name && p2.Value is ")" && Peek(3).Value is "=>";
        }

        // `[number, string]` or `[x: number, y: string]`. An optional (`string?`) or rest (`...string[]`) element
        // has no fixed C# arity, so it is reported rather than guessed at.
        private ITsType ReadTuple()
        {
            Next();
            var elements = new List<TsTupleElement>();
            string? unsupported = null;
            while (!At("]") && !AtEnd)
            {
                if (Accept("..."))
                {
                    unsupported = "a tuple with a rest element";
                }

                string? label = null;
                if (Current.Kind == TokenKind.Name && (Peek(1).Value is ":" || (Peek(1).Value is "?" && Peek(2).Value is ":")))
                {
                    label = Next().Value;
                    if (Accept("?"))
                    {
                        unsupported = "a tuple with an optional element";
                    }

                    Next();
                }

                var type = ReadType();
                if (Accept("?"))
                {
                    unsupported = "a tuple with an optional element";
                }

                elements.Add(new TsTupleElement(label, type));
                if (!Accept(","))
                {
                    break;
                }
            }

            Accept("]");
            if (elements.Count < 2)
            {
                unsupported ??= "a tuple of fewer than two elements — return the value itself, or an array";
            }

            return unsupported is null ? new TsTuple(elements) : new TsUnsupported(unsupported);
        }

        private ITsType ReadParenthesized()
        {
            Next();
            var inner = ReadType();
            Accept(")");
            return inner;
        }

        private ITsType ReadFunctionType()
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
                         && (Peek(1).Value is ":" || (Peek(1).Value is "?" && Peek(2).Value is ":")))
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
