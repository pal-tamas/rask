using Rask.Core.Forms;

namespace Rask.Core.Tests.Forms;

public class EditContextEdgeTests
{
    [Fact]
    public void A_field_change_does_not_fire_ValidationStateChanged()
    {
        var ctx = new EditContext(new Model());
        var fid = new FieldIdentifier(ctx.Model, "Name");
        var validationFired = 0;
        ctx.ValidationStateChanged += () => validationFired++;

        ctx.NotifyFieldChanged(fid);

        Assert.Equal(0, validationFired);
    }

    [Fact]
    public void Validating_an_unregistered_field_does_not_throw_and_adds_no_messages()
    {
        var ctx = new EditContext(new Model());
        var fid = new FieldIdentifier(ctx.Model, "Missing");

        var ok = ctx.ValidateField(fid);

        Assert.True(ok);
        Assert.Empty(ctx.GetValidationMessages(fid));
    }

    [Fact]
    public void Clearing_messages_removes_only_the_target_fields_messages()
    {
        var ctx = new EditContext(new Model());
        var a = new FieldIdentifier(ctx.Model, "A");
        var b = new FieldIdentifier(ctx.Model, "B");
        ctx.AddValidationMessage(a, "ax");
        ctx.AddValidationMessage(b, "bx");

        ctx.ClearMessages(a);

        Assert.Empty(ctx.GetValidationMessages(a));
        Assert.Single(ctx.GetValidationMessages(b));
    }

    [Fact]
    public void Clearing_a_field_with_no_messages_does_not_fire_the_validation_event()
    {
        var ctx = new EditContext(new Model());
        var fid = new FieldIdentifier(ctx.Model, "A");
        var fired = 0;
        ctx.ValidationStateChanged += () => fired++;

        ctx.ClearMessages(fid);

        Assert.Equal(0, fired);
    }

    [Fact]
    public void Clearing_all_messages_empties_every_field_and_fires_once()
    {
        var ctx = new EditContext(new Model());
        var a = new FieldIdentifier(ctx.Model, "A");
        var b = new FieldIdentifier(ctx.Model, "B");
        ctx.AddValidationMessage(a, "ax");
        ctx.AddValidationMessage(b, "bx");
        var fired = 0;
        ctx.ValidationStateChanged += () => fired++;

        ctx.ClearAllMessages();

        Assert.Empty(ctx.GetValidationMessages(a));
        Assert.Empty(ctx.GetValidationMessages(b));
        Assert.False(ctx.HasValidationMessages());
        Assert.Equal(1, fired);
    }

    [Fact]
    public void Clearing_all_messages_with_nothing_to_clear_does_not_fire()
    {
        var ctx = new EditContext(new Model());
        var fired = 0;
        ctx.ValidationStateChanged += () => fired++;

        ctx.ClearAllMessages();

        Assert.Equal(0, fired);
    }

    [Fact]
    public void Touching_all_registered_fields_marks_each_one_touched()
    {
        var ctx = new EditContext(new Model());
        var a = new FieldIdentifier(ctx.Model, "A");
        var b = new FieldIdentifier(ctx.Model, "B");
        ctx.NotifyFieldChanged(a);
        ctx.NotifyFieldChanged(b);

        ctx.TouchAllRegisteredFields();

        Assert.True(ctx.IsTouched(a));
        Assert.True(ctx.IsTouched(b));
    }

    [Fact]
    public void The_global_message_list_flattens_every_field()
    {
        var ctx = new EditContext(new Model());
        var a = new FieldIdentifier(ctx.Model, "A");
        var b = new FieldIdentifier(ctx.Model, "B");
        ctx.AddValidationMessage(a, "ax");
        ctx.AddValidationMessage(b, "by");

        var all = ctx.GetValidationMessages().OrderBy(m => m).ToArray();

        Assert.Equal(new[] { "ax", "by" }, all);
    }

    [Fact]
    public void A_duplicate_message_on_the_same_field_is_kept_once()
    {
        var ctx = new EditContext(new Model());
        var fid = new FieldIdentifier(ctx.Model, "A");

        ctx.AddValidationMessage(fid, "boom");
        ctx.AddValidationMessage(fid, "boom");

        Assert.Single(ctx.GetValidationMessages(fid));
    }

    [Fact]
    public void A_second_add_of_a_duplicate_message_does_not_fire_the_event()
    {
        var ctx = new EditContext(new Model());
        var fid = new FieldIdentifier(ctx.Model, "A");
        var fired = 0;
        ctx.ValidationStateChanged += () => fired++;

        ctx.AddValidationMessage(fid, "boom");
        ctx.AddValidationMessage(fid, "boom");

        Assert.Equal(1, fired);
    }

    [Fact]
    public void Distinct_messages_on_the_same_field_are_both_kept()
    {
        var ctx = new EditContext(new Model());
        var fid = new FieldIdentifier(ctx.Model, "A");

        ctx.AddValidationMessage(fid, "first");
        ctx.AddValidationMessage(fid, "second");

        Assert.Equal(new[] { "first", "second" }, ctx.GetValidationMessages(fid));
    }

    [Fact]
    public void The_same_message_on_different_fields_is_kept_on_both()
    {
        var ctx = new EditContext(new Model());
        var a = new FieldIdentifier(ctx.Model, "A");
        var b = new FieldIdentifier(ctx.Model, "B");

        ctx.AddValidationMessage(a, "same");
        ctx.AddValidationMessage(b, "same");

        Assert.Single(ctx.GetValidationMessages(a));
        Assert.Single(ctx.GetValidationMessages(b));
    }

    [Fact]
    public void A_null_model_throws() =>
        Assert.Throws<ArgumentNullException>(() => new EditContext(null!));

    [Fact]
    public void Adding_a_null_validator_throws()
    {
        var ctx = new EditContext(new Model());

        Assert.Throws<ArgumentNullException>(() => ctx.AddValidator((IFieldValidator)null!));
        Assert.Throws<ArgumentNullException>(() => ctx.AddValidator((IAsyncFieldValidator)null!));
    }

    private sealed class Model
    {
        public string Name { get; set; } = "";
    }
}
