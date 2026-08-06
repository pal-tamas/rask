namespace Rask.Cli.Tests;

public sealed class ArgumentSchemaTests
{
    private static ArgumentSchema Schema() =>
        new ArgumentSchema()
            .Option("template", 't')
            .Option("output", 'o')
            .Flag("auth")
            .Flag("docker");

    [Fact]
    public void Parses_positionals()
    {
        var parsed = Schema().Parse(["MyApp", "extra"]);

        Assert.Equal(["MyApp", "extra"], parsed.Positionals);
        Assert.False(parsed.HasErrors);
    }

    [Fact]
    public void Parses_long_option_with_separate_value()
    {
        var parsed = Schema().Parse(["--template", "wasm"]);

        Assert.Equal("wasm", parsed.Option("template"));
    }

    [Fact]
    public void Parses_long_option_with_equals()
    {
        var parsed = Schema().Parse(["--template=wasm"]);

        Assert.Equal("wasm", parsed.Option("template"));
    }

    [Fact]
    public void Parses_short_alias()
    {
        var parsed = Schema().Parse(["-t", "server", "-o", "out"]);

        Assert.Equal("server", parsed.Option("template"));
        Assert.Equal("out", parsed.Option("output"));
    }

    [Fact]
    public void Parses_boolean_flags()
    {
        var parsed = Schema().Parse(["--auth", "--docker"]);

        Assert.True(parsed.HasFlag("auth"));
        Assert.True(parsed.HasFlag("docker"));
        Assert.False(parsed.HasFlag("pwa"));
    }

    [Fact]
    public void Flag_with_explicit_false_is_not_set()
    {
        var parsed = Schema().Parse(["--auth=false"]);

        Assert.False(parsed.HasFlag("auth"));
        Assert.False(parsed.HasErrors);
    }

    [Fact]
    public void Everything_after_double_dash_is_passthrough()
    {
        var parsed = Schema().Parse(["--auth", "--", "--not-parsed", "value"]);

        Assert.True(parsed.HasFlag("auth"));
        Assert.Equal(["--not-parsed", "value"], parsed.Passthrough);
    }

    [Fact]
    public void Unknown_option_is_an_error()
    {
        var parsed = Schema().Parse(["--nope"]);

        Assert.True(parsed.HasErrors);
        Assert.Contains(parsed.Errors, e => e.Contains("--nope", StringComparison.Ordinal));
    }

    [Fact]
    public void Option_missing_value_is_an_error()
    {
        var parsed = Schema().Parse(["--template"]);

        Assert.True(parsed.HasErrors);
        Assert.Contains(parsed.Errors, e => e.Contains("requires a value", StringComparison.Ordinal));
    }

    [Fact]
    public void Option_does_not_swallow_a_following_flag_as_its_value()
    {
        // '--output --auth' must not set output="--auth"; it is a missing value (an error).
        // The following '--auth' is still parsed as its own flag, but the error aborts the command.
        var parsed = Schema().Parse(["--output", "--auth"]);

        Assert.True(parsed.HasErrors);
        Assert.Contains(parsed.Errors, e => e.Contains("requires a value", StringComparison.Ordinal));
        Assert.Null(parsed.Option("output"));
    }

    [Fact]
    public void Option_value_may_be_a_negative_number()
    {
        var parsed = Schema().Parse(["--output", "-5"]);

        Assert.Equal("-5", parsed.Option("output"));
        Assert.False(parsed.HasErrors);
    }

    [Fact]
    public void Declared_records_each_option_for_help()
    {
        var schema = new ArgumentSchema()
            .Option("template", 't', "name", "Which template.")
            .Flag("auth", description: "Add auth.", group: "Extras");

        var template = schema.Declared.Single(o => o.LongName == "template");
        Assert.Equal('t', template.ShortName);
        Assert.False(template.IsFlag);
        Assert.Equal("name", template.ValueHint);
        Assert.Equal("Which template.", template.Description);

        var auth = schema.Declared.Single(o => o.LongName == "auth");
        Assert.True(auth.IsFlag);
        Assert.Equal("Extras", auth.Group);
    }

    private static ArgumentSchema WithChoices() =>
        new ArgumentSchema().Option("template", 't', choices: ["server", "wasm", "wasm-hosted"]);

    [Fact]
    public void A_declared_choice_is_accepted()
    {
        var parsed = WithChoices().Parse(["--template", "wasm"]);

        Assert.False(parsed.HasErrors);
        Assert.Equal("wasm", parsed.Option("template"));
    }

    [Fact]
    public void A_choice_is_normalized_to_its_declared_spelling()
    {
        // Everything downstream compares ordinally, so the command must never see "SERVER".
        var parsed = WithChoices().Parse(["--template", "SERVER"]);

        Assert.False(parsed.HasErrors);
        Assert.Equal("server", parsed.Option("template"));
    }

    [Fact]
    public void An_off_list_choice_is_rejected_with_the_set_and_the_nearest_match()
    {
        var parsed = WithChoices().Parse(["--template", "srever"]);

        var error = Assert.Single(parsed.Errors);
        Assert.Equal(
            "Option '--template' does not accept 'srever'. Did you mean 'server'? Choose one of: server, wasm, wasm-hosted.",
            error);
        Assert.Null(parsed.Option("template"));
    }

    [Fact]
    public void An_off_list_choice_with_no_near_match_still_lists_the_set()
    {
        var parsed = WithChoices().Parse(["--template", "svelte"]);

        Assert.Equal(
            "Option '--template' does not accept 'svelte'. Choose one of: server, wasm, wasm-hosted.",
            Assert.Single(parsed.Errors));
    }

    [Fact]
    public void An_unknown_long_option_suggests_the_nearest_declared_one() =>
        Assert.Equal(
            "Unknown option '--tempate'. Did you mean '--template'?",
            Assert.Single(WithChoices().Parse(["--tempate", "wasm"]).Errors));

    [Fact]
    public void An_unknown_short_option_is_not_guessed_at() =>
        Assert.Equal("Unknown option '-z'.", Assert.Single(WithChoices().Parse(["-z", "wasm"]).Errors));

    [Fact]
    public void Verbs_resolve_by_name_and_by_alias()
    {
        var schema = new ArgumentSchema()
            .Verb("feature", "A CRUD slice.", "f")
            .Verb("cache", "A cached read.", "ca");

        Assert.True(schema.TryResolveVerb("feature", out var byName));
        Assert.Equal("feature", byName);

        Assert.True(schema.TryResolveVerb("ca", out var byAlias));
        Assert.Equal("cache", byAlias);

        Assert.False(schema.TryResolveVerb("controller", out _));
        Assert.False(schema.TryResolveVerb(null, out _));
    }

    [Fact]
    public void Verbs_keep_their_declaration_order_and_descriptions()
    {
        var schema = new ArgumentSchema()
            .Verb("add", "Create a migration.")
            .Verb("drop", "Delete the database.", "d");

        Assert.Equal(["add", "drop"], schema.Verbs.Select(v => v.Name));
        Assert.Equal("Create a migration.", schema.Verbs[0].Description);
        Assert.Equal(["d"], schema.Verbs[1].Aliases);
    }
}
