"""What the MDN snapshot's shape and order were first written in JavaScript terms, said once in Python.

The snapshot is a committed file a daily follower rewrites, so a refresh that only reorders it is noise in every
diff. Its order is the Unicode collation JavaScript's localeCompare uses (CLDR root: punctuation before digits
before letters, a letter's case only breaking a tie, lowercase first), its truthiness is JavaScript's (an empty
list is a value), and a field left undefined is left out of the JSON while keeping its place.
"""
import json
import math

# CLDR root's primary order for printable ASCII; a letter's two cases share one weight.
_PRIMARY = " _-,;:!?.'\"()[]{}@*/\\&#%`^+<=>|~$0123456789abcdefghijklmnopqrstuvwxyz"
_WEIGHT = {c: i for i, c in enumerate(_PRIMARY)}
for _upper in "ABCDEFGHIJKLMNOPQRSTUVWXYZ":
    _WEIGHT[_upper] = _WEIGHT[_upper.lower()]


def collate(text):
    """A sort key that orders names as localeCompare does: primary weights first, then lowercase before upper."""
    try:
        primary = tuple(_WEIGHT[c] for c in text)
    except KeyError as error:
        raise ValueError(f"{text!r}: {error} is outside the ASCII the collation table covers") from None
    return primary, tuple(1 if c.isupper() else 0 for c in text)


class _Undefined:
    """A field JavaScript would hold as undefined: it keeps its place in an object and is left out of the JSON."""

    def __repr__(self):
        return "undefined"

    def __bool__(self):
        return False


UNDEFINED = _Undefined()


def truthy(value):
    """JavaScript's truthiness: an empty list or object is a value; 0, "", null and undefined are not."""
    if value is None or value is False or value is UNDEFINED:
        return False
    if isinstance(value, bool):
        return True
    if isinstance(value, (int, float)):
        return value != 0 and not (isinstance(value, float) and math.isnan(value))
    if isinstance(value, str):
        return value != ""
    return True


def either(value):
    """`value || undefined`."""
    return value if truthy(value) else UNDEFINED


def number(text):
    """JavaScript's Number(text) for the integers and decimals IDL writes, as JSON.stringify prints it."""
    text = text.strip()
    if text.lower().startswith(("0x", "-0x")):
        value = int(text, 16)
    else:
        value = float(text)
    return int(value) if float(value).is_integer() else value


def _strip(value):
    if isinstance(value, dict):
        return {k: _strip(v) for k, v in value.items() if v is not UNDEFINED}
    if isinstance(value, (list, tuple)):
        return [None if v is UNDEFINED else _strip(v) for v in value]
    return value


def stringify(value, indent):
    """JSON.stringify(value, null, indent)."""
    return json.dumps(_strip(value), indent=indent, ensure_ascii=False)
