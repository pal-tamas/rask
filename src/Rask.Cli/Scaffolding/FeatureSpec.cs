namespace Rask.Cli.Scaffolding;

/// <summary>
/// The shape of a relationship between two generated entities, as written on the command line
/// (<c>rask g f Post Title:string 1:n Comment Body:text</c>). <see cref="RelationshipSpec.IsOptional"/>
/// carries the nullability a <c>0</c> in the token asks for, so <c>1:n</c> and <c>0:n</c> share this enum.
/// </summary>
internal enum Cardinality
{
    /// <summary><c>1:n</c> / <c>0:n</c> — one root, many targets. The target holds the foreign key.</summary>
    OneToMany,

    /// <summary><c>n:1</c> / <c>n:0</c> — many roots, one target. The <b>root</b> holds the foreign key.</summary>
    ManyToOne,

    /// <summary><c>1:1</c> / <c>0:1</c> — one root, one target. The target holds a unique foreign key.</summary>
    OneToOne,

    /// <summary><c>n:n</c> — many to many, through a generated join entity.</summary>
    ManyToMany,
}

/// <summary>One entity in a <see cref="FeatureSpec"/> — the root, or a relationship's target.</summary>
internal sealed record EntitySpec(string Name, string Plural, IReadOnlyList<FieldSpec> Fields);

/// <summary>
/// One <c>&lt;card&gt; &lt;Target&gt;</c> segment. <see cref="From"/> is always the feature's root (relationships
/// form a star — segments never chain), and <see cref="To"/> is the target the segment named.
/// </summary>
internal sealed record RelationshipSpec(Cardinality Cardinality, bool IsOptional, EntitySpec From, EntitySpec To)
{
    /// <summary>
    /// The side that owns the key the relationship points at. It's the target only for <c>n:1</c>/<c>n:0</c>,
    /// where the root is the "many" end; every other cardinality keeps the root as the principal.
    /// </summary>
    public EntitySpec Principal => Cardinality is Cardinality.ManyToOne ? To : From;

    /// <summary>The side that carries the foreign key. Meaningless for <see cref="Cardinality.ManyToMany"/>.</summary>
    public EntitySpec Dependent => Cardinality is Cardinality.ManyToOne ? From : To;

    /// <summary>The foreign-key property on <see cref="Dependent"/>, e.g. <c>PostId</c>.</summary>
    public string ForeignKeyName => Principal.Name + "Id";

    /// <summary>The generated join entity for an <c>n:n</c>, e.g. <c>PostTag</c>.</summary>
    public string JoinName => From.Name + To.Name;

    /// <summary>The token this relationship was written as, e.g. <c>0:n</c> — for echoing back in messages.</summary>
    public string Token => (Cardinality, IsOptional) switch
    {
        (Cardinality.OneToMany, false) => "1:n",
        (Cardinality.OneToMany, true) => "0:n",
        (Cardinality.ManyToOne, false) => "n:1",
        (Cardinality.ManyToOne, true) => "n:0",
        (Cardinality.OneToOne, false) => "1:1",
        (Cardinality.OneToOne, true) => "0:1",
        _ => "n:n",
    };

    /// <summary>
    /// Every member this relationship adds, as (entity, member) pairs. The single source both the validator
    /// (to reject a user field that would collide) and the generator (to emit them) read, so the two can't
    /// disagree about what a relationship produces.
    /// </summary>
    public IEnumerable<(string Entity, string Member)> GeneratedMembers()
    {
        if (Cardinality is Cardinality.ManyToMany)
        {
            yield return (From.Name, To.Plural);   // Post.Tags
            yield return (To.Name, From.Plural);   // Tag.Posts
            yield break;
        }

        var principal = Principal;
        var dependent = Dependent;

        yield return (dependent.Name, ForeignKeyName);  // Comment.PostId
        yield return (dependent.Name, principal.Name);  // Comment.Post

        // The principal's back-reference: a collection, except for 1:1 where it's a single reference.
        yield return (principal.Name, Cardinality is Cardinality.OneToOne ? dependent.Name : dependent.Plural);
    }
}

/// <summary>
/// A whole <c>generate feature</c> request: the root entity plus every relationship segment that followed it.
/// The key type isn't here — one <c>--id</c> governs the run, so it lives on the generator's options and a
/// foreign key can't disagree with the primary key it points at.
/// </summary>
internal sealed record FeatureSpec(EntitySpec Root, IReadOnlyList<RelationshipSpec> Relationships)
{
    /// <summary>The root followed by each relationship's target, in the order they were written.</summary>
    public IEnumerable<EntitySpec> Entities => [Root, .. Relationships.Select(r => r.To)];
}
