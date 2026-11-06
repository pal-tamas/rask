"""A Web IDL parser (https://webidl.spec.whatwg.org/#idl-grammar) for the MDN refresh: @webref/idl's files in, the
definitions refresh.py merges out.

It reads what webidl2, the parser webref validates its IDL with, reads, and names things the way webidl2 does
(a leading `_` escape dropped, `unsigned long long` one type name, an extended attribute's right-hand side as
`{type, value}`), so the snapshot written from it is the one webidl2 gave. Only the parts the snapshot uses are
kept: names, types, arguments, extended attributes and flags, never a default's value or the source's trivia.
webref's IDL is validated before it is published, so a file this cannot read is an error, not a recovery.
"""
import re

_TOKENS = {
    "decimal": re.compile(r"-?(?=[0-9]*\.|[0-9]+[eE])(([0-9]+\.[0-9]*|[0-9]*\.[0-9]+)([Ee][-+]?[0-9]+)?|[0-9]+[Ee][-+]?[0-9]+)"),
    "integer": re.compile(r"-?(0([Xx][0-9A-Fa-f]+|[0-7]*)|[1-9][0-9]*)"),
    "identifier": re.compile(r"[_-]?[A-Za-z][0-9A-Z_a-z-]*"),
    "string": re.compile(r'"[^"]*"'),
}
_TRIVIA = re.compile(r"[\t\n\r ]+|//.*|/\*[\s\S]*?\*/")
_PUNCTUATION = ["(", ")", ",", "...", ":", ";", "<", "=", ">", "?", "*", "[", "]", "{", "}"]

TYPE_NAME_KEYWORDS = ["ArrayBuffer", "SharedArrayBuffer", "DataView", "Int8Array", "Int16Array", "Int32Array", "Uint8Array",
                      "Uint16Array", "Uint32Array", "Uint8ClampedArray", "BigInt64Array", "BigUint64Array", "Float16Array",
                      "Float32Array", "Float64Array", "any", "object", "symbol"]
STRING_TYPES = ["ByteString", "DOMString", "USVString"]
ARGUMENT_NAME_KEYWORDS = ["async", "attribute", "callback", "const", "constructor", "deleter", "dictionary", "enum", "getter",
                          "includes", "inherit", "interface", "iterable", "maplike", "namespace", "partial", "required",
                          "setlike", "setter", "static", "stringifier", "typedef", "unrestricted"]
# Words the grammar spells out, which are keywords rather than identifiers wherever they appear.
_KEYWORDS = frozenset(["-Infinity", "FrozenArray", "Infinity", "NaN", "ObservableArray", "Promise", "async_iterable",
                       "async_sequence", "bigint", "boolean", "byte", "double", "false", "float", "long", "mixin", "null",
                       "octet", "optional", "or", "readonly", "record", "sequence", "short", "true", "undefined", "unsigned",
                       "void", *ARGUMENT_NAME_KEYWORDS, *STRING_TYPES, *TYPE_NAME_KEYWORDS])
_GENERICS = ["FrozenArray", "ObservableArray", "Promise", "async_sequence", "sequence", "record"]


def unescape(identifier):
    return identifier[1:] if identifier.startswith("_") else identifier


class Node:
    """One parsed production. Fields a production does not have read as webidl2's absent ones do."""

    name = None
    special = ""
    readonly = False
    partial = False
    inheritance = None
    idlType = None
    required = False
    optional = False
    variadic = False

    def __init__(self, type, **fields):
        self.type = type
        self.extAttrs = []
        self.members = []
        self.arguments = []
        self.__dict__.update(fields)


class Type:
    """An IDL type: `idlType` a name, or the list of a union's members or a generic's parameters."""

    def __init__(self, idlType, generic="", union=False, nullable=False):
        self.idlType, self.generic, self.union, self.nullable = idlType, generic, union, nullable


class ExtAttr:
    def __init__(self, name, rhs, arguments):
        self.name, self.rhs, self.arguments = name, rhs, arguments


def tokenise(text):
    tokens = []
    at = 0
    while at < len(text):
        trivia = _TRIVIA.match(text, at)
        if trivia:
            at = trivia.end()
            continue
        token = None
        if re.match(r"[-0-9.A-Z_a-z]", text[at]):
            for kind in ("decimal", "integer", "identifier"):
                match = _TOKENS[kind].match(text, at)
                if match:
                    value = match.group(0)
                    if kind == "identifier" and value in ("_constructor", "toString", "_toString"):
                        raise SyntaxError(f"{unescape(value)} is a reserved identifier and must not be used")
                    token = ("inline" if kind == "identifier" and value in _KEYWORDS else kind, value)
                    break
        elif text[at] == '"':
            match = _TOKENS["string"].match(text, at)
            if match:
                token = ("string", match.group(0))
        if token is None:
            value = next((p for p in _PUNCTUATION if text.startswith(p, at)), None)
            token = ("inline", value) if value else ("other", text[at])
        tokens.append(token)
        at += len(token[1])
    tokens.append(("eof", ""))
    return tokens


class Parser:
    def __init__(self, text):
        self.tokens = tokenise(text)
        self.at = 0

    # ---- the token stream -------------------------------------------------------------------------
    def error(self, message):
        raise SyntaxError(f"{message} at token {self.at}: {self.tokens[self.at][1]!r}")

    def probe(self, value):
        kind, text = self.tokens[self.at]
        return kind == "inline" and text == value

    def consume(self, *values):
        kind, text = self.tokens[self.at]
        if kind == "inline" and text in values:
            self.at += 1
            return text
        return None

    def consume_kind(self, *kinds):
        kind, text = self.tokens[self.at]
        if kind in kinds:
            self.at += 1
            return text
        return None

    def expect(self, *values):
        return self.consume(*values) or self.error(f"expected {' or '.join(values)}")

    def identifier(self, what):
        return self.consume_kind("identifier") or self.error(f"{what} lacks a name")

    def comma_list(self, parse):
        items = []
        item = parse()
        while item is not None:
            items.append(item)
            if not self.consume(","):
                break
            item = parse()
        return items

    # ---- definitions ------------------------------------------------------------------------------
    def definitions(self):
        defs = []
        while True:
            ext_attrs = self.ext_attrs()
            definition = self.definition()
            if definition is None:
                if ext_attrs:
                    self.error("stray extended attributes")
                break
            definition.extAttrs = ext_attrs
            defs.append(definition)
        if self.tokens[self.at][0] != "eof":
            self.error("unrecognised tokens")
        return defs

    def definition(self):
        if self.consume("callback"):
            if self.consume("interface"):
                return self.container("callback interface", False, [self.constant, lambda: self.operation(regular=True)])
            name = unescape(self.identifier("callback"))
            self.expect("=")
            returns = self.return_type()
            self.expect("(")
            arguments = self.argument_list()
            self.expect(")")
            self.expect(";")
            return Node("callback", name=name, idlType=returns, arguments=arguments)
        if self.consume("interface"):
            return self.interface(False)
        if self.consume("partial"):
            if self.consume("dictionary"):
                return self.dictionary(True)
            if self.consume("interface"):
                return self.interface(True)
            if self.consume("namespace"):
                return self.namespace(True)
            self.error("partial doesn't apply to anything")
        if self.consume("dictionary"):
            return self.dictionary(False)
        if self.consume("enum"):
            name = unescape(self.identifier("enum"))
            self.expect("{")
            values = []
            while True:
                value = self.consume_kind("string")
                if value is None:
                    break
                values.append(value[1:-1])
                if not self.consume(","):
                    break
            self.expect("}")
            self.expect(";")
            return Node("enum", name=name, values=values)
        if self.consume("typedef"):
            idl_type = self.type_with_ext_attrs()
            name = unescape(self.identifier("typedef"))
            self.expect(";")
            return Node("typedef", name=name, idlType=idl_type)
        start = self.at
        target = self.consume_kind("identifier")
        if target:
            if not self.consume("includes"):
                self.at = start
                return None
            mixin = self.identifier("includes")
            self.expect(";")
            return Node("includes", target=unescape(target), includes=unescape(mixin))
        if self.consume("namespace"):
            return self.namespace(False)
        return None

    def interface(self, partial):
        if self.consume("mixin"):
            return self.container("interface mixin", False, [
                self.constant, self.stringifier, lambda: self.attribute(no_inherit=True), lambda: self.operation(regular=True)],
                partial=partial)
        return self.container("interface", not partial, [
            self.constant, self.constructor, self.static_member, self.stringifier, self.iterable_like, self.attribute,
            self.operation], partial=partial)

    def dictionary(self, partial):
        return self.container("dictionary", not partial, [self.field], partial=partial)

    def namespace(self, partial):
        return self.container("namespace", False, [
            lambda: self.attribute(no_inherit=True), self.constant, lambda: self.operation(regular=True)], partial=partial)

    def container(self, type, inheritable, members, partial=False):
        node = Node(type, name=unescape(self.identifier(type)), partial=partial)
        if inheritable and self.consume(":"):
            node.inheritance = unescape(self.consume_kind("identifier") or self.error("inheritance lacks a type"))
        self.expect("{")
        while not self.consume("}"):
            ext_attrs = self.ext_attrs()
            member = next((m for m in (parse() for parse in members) if m is not None), None)
            if member is None:
                self.error(f"unknown member in {type} {node.name}")
            member.extAttrs = ext_attrs
            node.members.append(member)
        self.expect(";")
        return node

    # ---- members ----------------------------------------------------------------------------------
    def constant(self):
        if not self.consume("const"):
            return None
        idl_type = self.primitive_type() or Type(unescape(self.consume_kind("identifier") or self.error("const lacks a type")))
        name = unescape(self.identifier("const"))
        self.expect("=")
        self.const_value() or self.error("const lacks a value")
        self.expect(";")
        return Node("const", name=name, idlType=idl_type)

    def constructor(self):
        if not self.consume("constructor"):
            return None
        self.expect("(")
        arguments = self.argument_list()
        self.expect(")")
        self.expect(";")
        return Node("constructor", arguments=arguments)

    def static_member(self):
        special = self.consume("static")
        if not special:
            return None
        return self.attribute(special=special) or self.operation(special=special) or self.error("no body in static member")

    def stringifier(self):
        special = self.consume("stringifier")
        if not special:
            return None
        return self.attribute(special=special) or self.operation(special=special) or self.error("unterminated stringifier")

    def iterable_like(self):
        start = self.at
        readonly = bool(self.consume("readonly"))
        is_async = not readonly and bool(self.consume("async"))
        if readonly:
            base = self.consume("maplike", "setlike")
        elif is_async:
            base = self.consume("iterable")
        else:
            base = self.consume("iterable", "async_iterable", "maplike", "setlike")
        if not base:
            self.at = start
            return None
        self.expect("<")
        types = [self.type_with_ext_attrs()]
        if base != "setlike" and self.consume(","):
            types.append(self.type_with_ext_attrs())
        self.expect(">")
        arguments = []
        if self.consume("("):
            arguments = self.argument_list()
            self.expect(")")
        self.expect(";")
        return Node(base, idlType=types, arguments=arguments, readonly=readonly)

    def attribute(self, special=None, no_inherit=False):
        start = self.at
        if not special and not no_inherit:
            special = self.consume("inherit")
        readonly = bool(self.consume("readonly"))
        if not self.consume("attribute"):
            self.at = start
            return None
        idl_type = self.type_with_ext_attrs() or self.error("attribute lacks a type")
        name = self.consume_kind("identifier") or self.consume("async", "required") or self.error("attribute lacks a name")
        self.expect(";")
        return Node("attribute", name=unescape(name), special=special or "", readonly=readonly, idlType=idl_type)

    def operation(self, special=None, regular=False):
        if special == "stringifier" and self.consume(";"):
            return Node("operation", name="", special=special)
        if not special and not regular:
            special = self.consume("getter", "setter", "deleter")
        returns = self.return_type() or self.error("missing return type")
        name = self.consume_kind("identifier") or self.consume("includes")
        self.expect("(")
        arguments = self.argument_list()
        self.expect(")")
        self.expect(";")
        return Node("operation", name=unescape(name) if name else "", special=special or "", idlType=returns, arguments=arguments)

    def field(self):
        ext_attrs = self.ext_attrs()
        required = bool(self.consume("required"))
        idl_type = self.type_with_ext_attrs() or self.error("dictionary member lacks a type")
        name = unescape(self.identifier("dictionary member"))
        self.default()
        self.expect(";")
        node = Node("field", name=name, required=required, idlType=idl_type)
        node.extAttrs = ext_attrs
        return node

    # ---- arguments, values and extended attributes ------------------------------------------------
    def argument(self):
        start = self.at
        ext_attrs = self.ext_attrs()
        optional = bool(self.consume("optional"))
        idl_type = self.type_with_ext_attrs()
        if idl_type is None:
            self.at = start
            return None
        variadic = not optional and bool(self.consume("..."))
        name = self.consume_kind("identifier") or self.consume(*ARGUMENT_NAME_KEYWORDS)
        if not name:
            self.at = start
            return None
        if optional:
            self.default()
        node = Node("argument", name=unescape(name), optional=optional, variadic=variadic, idlType=idl_type)
        node.extAttrs = ext_attrs
        return node

    def argument_list(self):
        return self.comma_list(self.argument)

    def const_value(self):
        return self.consume_kind("decimal", "integer") or self.consume("true", "false", "Infinity", "-Infinity", "NaN")

    def default(self):
        if not self.consume("="):
            return
        value = self.const_value() or self.consume_kind("string") or self.consume("null", "[", "{") or self.error("no value for default")
        if value == "[":
            self.expect("]")
        elif value == "{":
            self.expect("}")

    def ext_attrs(self):
        if not self.consume("["):
            return []
        attrs = self.comma_list(self.ext_attr)
        self.expect("]")
        if not attrs:
            self.error("an extended attribute list must not be empty")
        return attrs

    def ext_attr(self):
        name = self.consume_kind("identifier")
        if not name:
            return None
        rhs, arguments = None, []
        assign = self.consume("=")
        if assign and self.consume("*"):
            return ExtAttr(name, {"type": "*", "value": None}, [])
        secondary = None
        if assign:
            kind = self.tokens[self.at][0]
            secondary = self.consume_kind("identifier", "decimal", "integer", "string")
            if secondary is not None:
                rhs = {"type": kind, "value": unescape(secondary)}
        if self.consume("("):
            if assign and secondary is None:
                for kind in ("identifier", "decimal", "integer", "string"):
                    items = self.comma_list(lambda k=kind: self.consume_kind(k))
                    if items:
                        rhs = {"type": kind + "-list", "value": [unescape(i) for i in items]}
                        break
                else:
                    self.error("expected identifiers, strings, decimals, or integers")
            else:
                arguments = self.argument_list()
            self.expect(")")
        elif assign and secondary is None:
            self.error("no right hand side to extended attribute assignment")
        return ExtAttr(name, rhs, arguments)

    # ---- types ------------------------------------------------------------------------------------
    def type_with_ext_attrs(self):
        self.ext_attrs()
        return self.type()

    def return_type(self):
        idl_type = self.type()
        if idl_type is not None:
            return idl_type
        return Type("void") if self.consume("void") else None

    def type(self):
        return self.single_type() or self.union_type()

    def single_type(self):
        idl_type = self.generic_type() or self.primitive_type()
        if idl_type is None:
            base = self.consume_kind("identifier") or self.consume(*STRING_TYPES, *TYPE_NAME_KEYWORDS)
            if not base:
                return None
            idl_type = Type(unescape(base))
        idl_type.nullable = bool(self.consume("?"))
        return idl_type

    def generic_type(self):
        base = self.consume(*_GENERICS)
        if not base:
            return None
        self.expect("<")
        if base == "Promise":
            parameters = [self.return_type() or self.error("missing Promise subtype")]
        elif base == "record":
            key = self.consume(*STRING_TYPES) or self.error("record key must be a string type")
            self.expect(",")
            parameters = [Type(key), self.type_with_ext_attrs() or self.error("error parsing record")]
        else:
            parameters = [self.type_with_ext_attrs() or self.error(f"missing {base} subtype")]
        self.expect(">")
        return Type(parameters, generic=base)

    def primitive_type(self):
        prefix = self.consume("unsigned")
        base = self.consume("short", "long")
        if base:
            postfix = self.consume("long")
            return Type(" ".join(t for t in (prefix, base, postfix) if t))
        if prefix:
            self.error("failed to parse integer type")
        prefix = self.consume("unrestricted")
        base = self.consume("float", "double")
        if base:
            return Type(" ".join(t for t in (prefix, base) if t))
        if prefix:
            self.error("failed to parse float type")
        base = self.consume("bigint", "boolean", "byte", "octet", "undefined")
        return Type(base) if base else None

    def union_type(self):
        if not self.consume("("):
            return None
        members = [self.type_with_ext_attrs() or self.error("no type in union")]
        while self.consume("or"):
            members.append(self.type_with_ext_attrs() or self.error("no type after 'or' in union"))
        self.expect(")")
        return Type(members, union=True, nullable=bool(self.consume("?")))


def parse(text):
    """Every definition in one IDL file, in source order."""
    return Parser(text).definitions()
