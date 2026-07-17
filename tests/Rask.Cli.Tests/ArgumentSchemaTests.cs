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
}
