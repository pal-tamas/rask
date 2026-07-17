using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

public sealed class FeatureSpecParserTests
{
    private static FeatureSpec Parse(params string[] tokens)
    {
        Assert.True(FeatureSpecParser.TryParse("Post", null, tokens, out var spec, out var error), error);
        return spec;
    }

    private static string Error(params string[] tokens)
    {
        Assert.False(FeatureSpecParser.TryParse("Post", null, tokens, out _, out var error));
        Assert.NotNull(error);
        return error!;
    }

    // ---- the no-relationship case is exactly what it was before the grammar grew ----

    [Fact]
    public void A_feature_with_no_relationships_is_just_a_root()
    {
        var spec = Parse("Title:string", "Views:int");

        Assert.Empty(spec.Relationships);
        Assert.Equal("Post", spec.Root.Name);
        Assert.Equal("Posts", spec.Root.Plural);
        Assert.Equal(["Title", "Views"], spec.Root.Fields.Select(f => f.Name));
    }

    // ---- segmenting ----

    [Fact]
    public void Fields_bind_to_the_segment_that_precedes_them()
    {
        var spec = Parse("Title:string", "1:n", "Comment", "Body:string", "Author:string");

        Assert.Equal(["Title"], spec.Root.Fields.Select(f => f.Name));
        var comment = Assert.Single(spec.Relationships).To;
        Assert.Equal("Comment", comment.Name);
        Assert.Equal(["Body", "Author"], comment.Fields.Select(f => f.Name));
    }

    [Fact]
    public void Every_relationship_attaches_to_the_root_segments_do_not_chain()
    {
        // `Post 1:n Comment n:1 Author` means Author is POST's author — not Comment's.
        var spec = Parse("Title:string", "1:n", "Comment", "Body:string", "n:1", "Author", "Name:string");

        Assert.Equal(2, spec.Relationships.Count);
        Assert.All(spec.Relationships, r => Assert.Equal("Post", r.From.Name));
        Assert.Equal(["Comment", "Author"], spec.Relationships.Select(r => r.To.Name));
    }

    [Fact]
    public void The_root_and_every_target_are_entities()
    {
        var spec = Parse("Title:string", "1:n", "Comment", "Body:string", "n:n", "Tag", "Name:string");

        Assert.Equal(["Post", "Comment", "Tag"], spec.Entities.Select(e => e.Name));
    }

    // ---- cardinality tokens ----

    // Cardinality is internal, so the shape travels as its member name rather than the enum itself.
    [Theory]
    [InlineData("1:n", nameof(Cardinality.OneToMany), false)]
    [InlineData("0:n", nameof(Cardinality.OneToMany), true)]
    [InlineData("n:1", nameof(Cardinality.ManyToOne), false)]
    [InlineData("n:0", nameof(Cardinality.ManyToOne), true)]
    [InlineData("1:1", nameof(Cardinality.OneToOne), false)]
    [InlineData("0:1", nameof(Cardinality.OneToOne), true)]
    [InlineData("n:n", nameof(Cardinality.ManyToMany), false)]
    public void Each_cardinality_token_maps_to_a_shape_and_a_nullability(string token, string cardinality, bool isOptional)
    {
        var relationship = Assert.Single(Parse("Title:string", token, "Tag", "Name:string").Relationships);

        Assert.Equal(cardinality, relationship.Cardinality.ToString());
        Assert.Equal(isOptional, relationship.IsOptional);
        Assert.Equal(token, relationship.Token);
    }

    [Theory]
    [InlineData("1:N")]
    [InlineData("N:1")]
    [InlineData("N:N")]
    public void Cardinality_tokens_are_case_insensitive(string token) =>
        Assert.Single(Parse("Title:string", token, "Tag", "Name:string").Relationships);

    // ---- which side owns the key ----

    [Fact]
    public void One_to_many_puts_the_foreign_key_on_the_target()
    {
        var relationship = Assert.Single(Parse("Title:string", "1:n", "Comment", "Body:string").Relationships);

        Assert.Equal("Post", relationship.Principal.Name);
        Assert.Equal("Comment", relationship.Dependent.Name);
        Assert.Equal("PostId", relationship.ForeignKeyName);
    }

    [Fact]
    public void Many_to_one_flips_it_the_root_owns_the_foreign_key()
    {
        var relationship = Assert.Single(Parse("Title:string", "n:1", "Category", "Name:string").Relationships);

        Assert.Equal("Category", relationship.Principal.Name);
        Assert.Equal("Post", relationship.Dependent.Name);
        Assert.Equal("CategoryId", relationship.ForeignKeyName);
    }

    [Fact]
    public void Many_to_many_names_a_join_entity_after_both_sides()
    {
        var relationship = Assert.Single(Parse("Title:string", "n:n", "Tag", "Name:string").Relationships);

        Assert.Equal("PostTag", relationship.JoinName);
    }

    // ---- generated members ----

    [Fact]
    public void One_to_many_generates_a_collection_on_the_root_and_a_key_plus_reference_on_the_target()
    {
        var relationship = Assert.Single(Parse("Title:string", "1:n", "Comment", "Body:string").Relationships);

        Assert.Equal(
            [("Comment", "PostId"), ("Comment", "Post"), ("Post", "Comments")],
            relationship.GeneratedMembers());
    }

    [Fact]
    public void Many_to_one_generates_the_key_on_the_root_and_a_collection_on_the_target()
    {
        var relationship = Assert.Single(Parse("Title:string", "n:1", "Category", "Name:string").Relationships);

        Assert.Equal(
            [("Post", "CategoryId"), ("Post", "Category"), ("Category", "Posts")],
            relationship.GeneratedMembers());
    }

    [Fact]
    public void One_to_one_generates_a_single_reference_on_each_side_not_a_collection()
    {
        var relationship = Assert.Single(Parse("Title:string", "1:1", "Seo", "Slug:string").Relationships);

        Assert.Equal(
            [("Seo", "PostId"), ("Seo", "Post"), ("Post", "Seo")],
            relationship.GeneratedMembers());
    }

    [Fact]
    public void Many_to_many_generates_a_collection_on_both_sides()
    {
        var relationship = Assert.Single(Parse("Title:string", "n:n", "Tag", "Name:string").Relationships);

        Assert.Equal([("Post", "Tags"), ("Tag", "Posts")], relationship.GeneratedMembers());
    }

    // ---- naming ----

    [Fact]
    public void A_target_is_pluralized_automatically()
    {
        var relationship = Assert.Single(Parse("Title:string", "1:n", "Category", "Name:string").Relationships);

        Assert.Equal("Categories", relationship.To.Plural);
    }

    [Fact]
    public void Plural_override_applies_to_the_root_only()
    {
        Assert.True(FeatureSpecParser.TryParse("Person", "People", ["Name:string", "1:n", "Category", "Label:string"], out var spec, out _));

        Assert.Equal("People", spec.Root.Plural);
        Assert.Equal("Categories", spec.Relationships[0].To.Plural);
    }

    // ---- fields are scoped per entity ----

    [Fact]
    public void The_same_field_name_may_appear_on_two_entities()
    {
        var spec = Parse("Name:string", "1:n", "Comment", "Name:string");

        Assert.Equal("Name", spec.Root.Fields[0].Name);
        Assert.Equal("Name", spec.Relationships[0].To.Fields[0].Name);
    }

    [Fact]
    public void A_duplicate_field_within_one_entity_is_still_rejected_and_names_it() =>
        Assert.Contains("Duplicate field 'Body'", Error("Title:string", "1:n", "Comment", "Body:string", "Body:int"), StringComparison.Ordinal);

    [Fact]
    public void A_field_error_on_a_target_names_the_entity() =>
        Assert.Contains("(on 'Comment')", Error("Title:string", "1:n", "Comment", "Body:wobble"), StringComparison.Ordinal);

    [Fact]
    public void A_field_error_with_no_relationships_reads_exactly_as_before() =>
        Assert.DoesNotContain("(on '", Error("Title:wobble"), StringComparison.Ordinal);

    // ---- errors ----

    [Fact]
    public void A_cardinality_at_the_end_has_no_target() =>
        Assert.Contains("needs a target entity name after it", Error("Title:string", "1:n"), StringComparison.Ordinal);

    [Fact]
    public void A_cardinality_followed_by_a_cardinality_is_a_missing_target() =>
        Assert.Contains("but found the cardinality 'n:1'", Error("Title:string", "1:n", "n:1", "Tag", "Name:string"), StringComparison.Ordinal);

    [Fact]
    public void A_cardinality_followed_by_a_field_is_a_missing_target() =>
        Assert.Contains("but found the field 'Body:string'", Error("Title:string", "1:n", "Body:string"), StringComparison.Ordinal);

    [Fact]
    public void A_target_must_be_a_valid_type_name() =>
        Assert.Contains("'2Comment' is not a valid entity name", Error("Title:string", "1:n", "2Comment", "Body:string"), StringComparison.Ordinal);

    [Fact]
    public void A_target_may_not_be_named_Id() =>
        Assert.Contains("every entity gets an Id automatically", Error("Title:string", "1:n", "Id", "Body:string"), StringComparison.Ordinal);

    [Fact]
    public void A_target_may_not_share_the_roots_name() =>
        Assert.Contains("can't share the root entity's name", Error("Title:string", "1:n", "Post", "Body:string"), StringComparison.Ordinal);

    [Fact]
    public void A_target_may_not_repeat() =>
        Assert.Contains("Duplicate target 'Tag'", Error("Title:string", "1:n", "Tag", "A:string", "n:n", "Tag", "B:string"), StringComparison.Ordinal);

    [Fact]
    public void A_target_needs_at_least_one_field() =>
        Assert.Contains("Target 'Comment' needs at least one field", Error("Title:string", "1:n", "Comment"), StringComparison.Ordinal);

    [Fact]
    public void A_root_with_no_fields_is_rejected() =>
        Assert.Contains("At least one field is required", Error("1:n", "Comment", "Body:string"), StringComparison.Ordinal);

    [Theory]
    [InlineData("1:m")]
    [InlineData("m:n")]
    [InlineData("n:m")]
    [InlineData("1:*")]
    [InlineData("2:n")]
    public void A_near_miss_cardinality_says_so_rather_than_blaming_the_field_name(string token)
    {
        var error = Error("Title:string", token, "Tag", "Name:string");

        Assert.Contains($"Unknown cardinality '{token}'", error, StringComparison.Ordinal);
        Assert.Contains("Supported: 1:n, 0:n, n:1, n:0, 1:1, 0:1, n:n", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_field_may_not_collide_with_a_generated_foreign_key() =>
        Assert.Contains(
            "Field 'PostId' on 'Comment' collides with the '1:n Comment' relationship's foreign key",
            Error("Title:string", "1:n", "Comment", "PostId:guid"),
            StringComparison.Ordinal);

    [Fact]
    public void A_field_may_not_collide_with_a_generated_navigation() =>
        Assert.Contains(
            "Field 'Comments' on 'Post' collides with the '1:n Comment' relationship's navigation property",
            Error("Comments:string", "1:n", "Comment", "Body:string"),
            StringComparison.Ordinal);

    [Fact]
    public void A_field_may_not_collide_with_a_generated_many_to_many_navigation() =>
        Assert.Contains(
            "Field 'Tags' on 'Post' collides with the 'n:n Tag' relationship's navigation property",
            Error("Tags:string", "n:n", "Tag", "Name:string"),
            StringComparison.Ordinal);

    [Fact]
    public void A_field_may_not_share_its_own_entitys_name() =>
        Assert.Contains(
            "Field 'Comment' can't share the entity's name 'Comment'",
            Error("Title:string", "1:n", "Comment", "Comment:string"),
            StringComparison.Ordinal);

    [Fact]
    public void A_join_entity_may_not_collide_with_a_real_entity() =>
        Assert.Contains(
            "would be named 'PostTag', which collides with the entity 'PostTag'",
            Error("Title:string", "n:n", "Tag", "Name:string", "1:n", "PostTag", "Note:string"),
            StringComparison.Ordinal);

    [Fact]
    public void Two_entities_may_not_share_a_plural()
    {
        Assert.False(FeatureSpecParser.TryParse("Post", "Comments", ["Title:string", "1:n", "Comment", "Body:string"], out _, out var error));

        Assert.Contains("both use the plural 'Comments'", error!, StringComparison.Ordinal);
    }
}
