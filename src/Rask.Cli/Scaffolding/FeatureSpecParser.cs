namespace Rask.Cli.Scaffolding;

/// <summary>
/// Parses a <c>generate feature</c> token stream — fields, plus optional <c>&lt;card&gt; &lt;Target&gt;</c>
/// relationship segments — into a <see cref="FeatureSpec"/>:
/// <code>
/// rask g f Post Title:string 1:n Comment Body:text n:n Tag Name:string
///          └─ root fields ─┘ └── segment ──┘ └─── segment ───┘
/// </code>
/// A cardinality token opens a new segment, and every field after it belongs to that segment's target.
/// Relationships form a <b>star</b>: each one attaches to the root, so segments never chain
/// (<c>Post 1:n Comment n:1 Author</c> means Author is <i>Post's</i> author, not Comment's).
/// </summary>
internal static class FeatureSpecParser
{
    // Recognising a cardinality before trying to parse a field is safe — and steals nothing that could
    // otherwise have parsed — because every card token is an invalid field spec: its type half ('n', '1',
    // '0') is not in FieldSpecParser.TypeAliases. Note the weaker "it's not an identifier" reasoning does
    // NOT hold: 'n' in `n:1` is a perfectly valid property name.
    private static readonly Dictionary<string, (Cardinality Cardinality, bool IsOptional)> Cards =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["1:n"] = (Cardinality.OneToMany, false),
            ["0:n"] = (Cardinality.OneToMany, true),
            ["n:1"] = (Cardinality.ManyToOne, false),
            ["n:0"] = (Cardinality.ManyToOne, true),
            ["1:1"] = (Cardinality.OneToOne, false),
            ["0:1"] = (Cardinality.OneToOne, true),
            ["n:n"] = (Cardinality.ManyToMany, false),
        };

    /// <summary>The cardinality tokens, in the order the help and error messages list them.</summary>
    public static IReadOnlyList<string> SupportedCardinalities { get; } =
        ["1:n", "0:n", "n:1", "n:0", "1:1", "0:1", "n:n"];

    /// <summary>
    /// Parses <paramref name="tokens"/> (everything after the root's name) into a <see cref="FeatureSpec"/>.
    /// <paramref name="rootPlural"/> is <c>--plural</c>, which applies to the root only — targets are
    /// pluralized automatically.
    /// </summary>
    public static bool TryParse(
        string rootName,
        string? rootPlural,
        IReadOnlyList<string> tokens,
        out FeatureSpec spec,
        out string? error)
    {
        spec = null!;

        if (!TrySegment(rootName, rootPlural, tokens, out var segments, out error)
            || !TryBuildEntities(segments, out var entities, out error)
            || !TryValidateNames(entities, out error))
        {
            return false;
        }

        var root = entities[0];
        var relationships = new List<RelationshipSpec>(entities.Count - 1);
        for (var i = 1; i < entities.Count; i++)
        {
            var (cardinality, isOptional) = Cards[segments[i].Card!];
            relationships.Add(new RelationshipSpec(cardinality, isOptional, root, entities[i]));
        }

        if (!TryValidateRelationships(entities, relationships, out error))
        {
            return false;
        }

        spec = new FeatureSpec(root, relationships);
        return true;
    }

    // Split the token stream into segments: the root, then one per `<card> <Target>` pair.
    private static bool TrySegment(
        string rootName,
        string? rootPlural,
        IReadOnlyList<string> tokens,
        out List<Segment> segments,
        out string? error)
    {
        segments = [new Segment(rootName, rootPlural, Card: null)];
        error = null;
        string? pendingCard = null;

        foreach (var token in tokens)
        {
            // A card is pending, so this token must name the target. Checked before the card lookup below,
            // so `1:n n:1` reports the missing name rather than silently dropping the first card.
            if (pendingCard is not null)
            {
                if (!TryOpenSegment(token, pendingCard, segments, out error))
                {
                    return false;
                }

                pendingCard = null;
                continue;
            }

            if (Cards.ContainsKey(token))
            {
                pendingCard = token;
                continue;
            }

            if (LooksLikeCardinality(token))
            {
                error = $"Unknown cardinality '{token}'. Supported: {string.Join(", ", SupportedCardinalities)}.";
                return false;
            }

            segments[^1].FieldTokens.Add(token);
        }

        if (pendingCard is not null)
        {
            error = $"Cardinality '{pendingCard}' needs a target entity name after it, e.g. {pendingCard} Comment Body:text.";
            return false;
        }

        return true;
    }

    private static bool TryOpenSegment(string token, string pendingCard, List<Segment> segments, out string? error)
    {
        if (Cards.ContainsKey(token))
        {
            error = $"Expected a target entity name after '{pendingCard}', but found the cardinality '{token}'.";
            return false;
        }

        if (token.Contains(':', StringComparison.Ordinal))
        {
            error = $"Expected a target entity name after '{pendingCard}', but found the field '{token}'.";
            return false;
        }

        if (token.Equals("Id", StringComparison.OrdinalIgnoreCase))
        {
            error = "'Id' can't be an entity name — every entity gets an Id automatically.";
            return false;
        }

        if (!Identifiers.IsValidTypeName(token))
        {
            error = $"'{token}' is not a valid entity name.";
            return false;
        }

        segments.Add(new Segment(token, PluralOverride: null, pendingCard));
        error = null;
        return true;
    }

    // Each segment's fields go through FieldSpecParser as one comma-joined spec — the same grammar the
    // root has always used, which also scopes duplicate-field detection to the entity for free.
    private static bool TryBuildEntities(List<Segment> segments, out List<EntitySpec> entities, out string? error)
    {
        entities = new List<EntitySpec>(segments.Count);
        error = null;

        foreach (var segment in segments)
        {
            if (segment.FieldTokens.Count == 0)
            {
                error = segment.Card is null
                    ? "At least one field is required, e.g. rask generate feature Product Name:string Price:decimal."
                    : $"Target '{segment.Name}' needs at least one field, e.g. {segment.Card} {segment.Name} Name:string.";
                return false;
            }

            if (!FieldSpecParser.TryParse(string.Join(",", segment.FieldTokens), out var fields, out var fieldError))
            {
                // Name the entity only when there's more than one, so a plain feature's message is unchanged.
                error = segments.Count > 1 ? $"{fieldError} (on '{segment.Name}')" : fieldError;
                return false;
            }

            entities.Add(new EntitySpec(segment.Name, segment.PluralOverride ?? Pluralizer.Pluralize(segment.Name), fields));
        }

        return true;
    }

    private static bool TryValidateNames(List<EntitySpec> entities, out string? error)
    {
        var root = entities[0];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var plurals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entity in entities)
        {
            if (!seen.Add(entity.Name))
            {
                error = entity.Name.Equals(root.Name, StringComparison.OrdinalIgnoreCase)
                    ? $"Target '{entity.Name}' can't share the root entity's name — a relationship needs two entities."
                    : $"Duplicate target '{entity.Name}' — each entity may appear once (one relationship per target).";
                return false;
            }

            // A property can't be named after the type that declares it.
            var collision = entity.Fields.FirstOrDefault(f => f.Name.Equals(entity.Name, StringComparison.OrdinalIgnoreCase));
            if (collision is not null)
            {
                error = $"Field '{collision.Name}' can't share the entity's name '{entity.Name}' (a member can't match its type).";
                return false;
            }

            if (!plurals.TryAdd(entity.Plural, entity.Name))
            {
                error = $"'{plurals[entity.Plural]}' and '{entity.Name}' both use the plural '{entity.Plural}' — their feature folders would collide. Pass --plural or rename one.";
                return false;
            }
        }

        error = null;
        return true;
    }

    private static bool TryValidateRelationships(
        List<EntitySpec> entities,
        List<RelationshipSpec> relationships,
        out string? error)
    {
        var names = entities.Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var relationship in relationships.Where(r => r.Cardinality is Cardinality.ManyToMany))
        {
            if (names.Contains(relationship.JoinName))
            {
                error = $"The n:n join entity for '{relationship.From.Name} n:n {relationship.To.Name}' would be named '{relationship.JoinName}', which collides with the entity '{relationship.JoinName}'. Rename one of them.";
                return false;
            }
        }

        // A user field can't take a name a relationship already generates.
        var generated = new Dictionary<string, RelationshipSpec>(StringComparer.OrdinalIgnoreCase);
        foreach (var relationship in relationships)
        {
            foreach (var (entity, member) in relationship.GeneratedMembers())
            {
                generated[entity + "." + member] = relationship;
            }
        }

        foreach (var entity in entities)
        {
            foreach (var field in entity.Fields)
            {
                if (!generated.TryGetValue(entity.Name + "." + field.Name, out var relationship))
                {
                    continue;
                }

                var kind = relationship.Cardinality is not Cardinality.ManyToMany
                    && field.Name.Equals(relationship.ForeignKeyName, StringComparison.OrdinalIgnoreCase)
                        ? "foreign key"
                        : "navigation property";

                error = $"Field '{field.Name}' on '{entity.Name}' collides with the '{relationship.Token} {relationship.To.Name}' relationship's {kind} — it's generated automatically.";
                return false;
            }
        }

        error = null;
        return true;
    }

    // A near-miss like `1:m`, `m:n`, `2:n`, or `1:*`: ER notation that isn't one of ours. Catching it beats
    // letting it fall through to the field parser, which would complain that '1' isn't a property name.
    // It can't shadow a real field — no multiplicity part is a supported field type.
    private static bool LooksLikeCardinality(string token)
    {
        var colon = token.IndexOf(':', StringComparison.Ordinal);
        return colon > 0
            && colon == token.LastIndexOf(':')
            && IsMultiplicity(token[..colon])
            && IsMultiplicity(token[(colon + 1)..]);
    }

    private static bool IsMultiplicity(string part) =>
        part.Length > 0
        && (part == "*"
            || part.All(char.IsAsciiDigit)
            || (part.Length == 1 && part[0] is 'n' or 'N' or 'm' or 'M'));

    private sealed record Segment(string Name, string? PluralOverride, string? Card)
    {
        public List<string> FieldTokens { get; } = [];
    }
}
