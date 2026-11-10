using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask.Core.Tests.Forms;

/// <summary>
///     What a form's context remembers of each bound field's first value: whether a field is modified
///     (<c>IsModified</c>, <c>IsModifiedAny</c>), and what <c>Reset</c> and <c>MarkSaved</c> do with it.
/// </summary>
public sealed class EditContextOriginalsTests
{
    private sealed class Address
    {
        public string City { get; set; } = "Pécs";
    }

    private sealed class Order
    {
        public string Name { get; set; } = "Tank";

        public int Litres { get; set; } = 40;

        public bool Urgent { get; set; }

        public List<string> Tags { get; set; } = ["red"];

        public string[] Codes { get; set; } = ["A"];

        public HashSet<string> Marks { get; set; } = ["x"];

        public Address Address { get; set; } = new();
    }

    // What a control does on its render: remember the field, and how to read and write it.
    private static FieldIdentifier Draw<T>(EditContext context, Expression<Func<T>> bind)
    {
        var accessor = ExpressionAccessor.Parse(bind);
        context.Remember(accessor.Field, accessor);
        return accessor.Field;
    }

    // What a control does when the reader changes it: the model written, the context told.
    private static void Type<T>(EditContext context, Expression<Func<T>> bind, T value)
    {
        var accessor = ExpressionAccessor.Parse(bind);
        accessor.Setter(value);
        context.NotifyFieldChanged(accessor.Field);
    }

    [Fact]
    public void A_form_nobody_changed_is_not_modified()
    {
        var order = new Order();
        var context = new EditContext(order);
        var name = Draw(context, () => order.Name);

        var any = context.IsModifiedAny;

        Assert.False(any);
        Assert.False(context.IsModified(name));
    }

    [Fact]
    public void A_field_typed_into_is_modified_and_typed_back_is_not()
    {
        var order = new Order();
        var context = new EditContext(order);
        var name = Draw(context, () => order.Name);

        Type(context, () => order.Name, "Barrel");
        var afterTyping = context.IsModifiedAny;
        Type(context, () => order.Name, "Tank");

        Assert.True(afterTyping);
        Assert.False(context.IsModifiedAny);
        Assert.False(context.IsModified(name));
    }

    [Fact]
    public void A_number_and_a_checkbox_compare_by_value()
    {
        var order = new Order();
        var context = new EditContext(order);
        Draw(context, () => order.Litres);
        Draw(context, () => order.Urgent);

        Type(context, () => order.Litres, 40);
        Type(context, () => order.Urgent, true);
        var urgent = context.IsModifiedAny;
        Type(context, () => order.Urgent, false);

        Assert.True(urgent);
        Assert.False(context.IsModifiedAny);
    }

    [Fact]
    public void A_list_changed_in_place_is_modified_until_it_holds_what_it_held()
    {
        var order = new Order();
        var context = new EditContext(order);
        var tags = Draw(context, () => order.Tags);

        order.Tags.Add("blue");
        context.NotifyFieldChanged(tags);
        var afterAdding = context.IsModifiedAny;
        order.Tags.Remove("blue");
        context.NotifyFieldChanged(tags);

        Assert.True(afterAdding);
        Assert.False(context.IsModifiedAny);
    }

    [Fact]
    public void A_field_of_a_nested_model_is_counted_as_its_own()
    {
        var order = new Order();
        var context = new EditContext(order);
        var city = Draw(context, () => order.Address.City);

        Type(context, () => order.Address.City, "Győr");

        Assert.True(context.IsModified(city));
        Assert.True(context.IsModifiedAny);
    }

    [Fact]
    public void A_pick_not_written_yet_is_compared_in_place_of_the_model()
    {
        var order = new Order();
        var context = new EditContext(order);
        var name = Draw(context, () => order.Name);

        context.NoteEdited(name, "Barrel");
        var pickedAnother = context.IsModifiedAny;
        context.NoteEdited(name, "Tank");

        Assert.True(pickedAnother);
        Assert.False(context.IsModifiedAny);
        Assert.Equal("Tank", order.Name);
    }

    [Fact]
    public void A_field_whose_text_could_not_be_read_is_modified_whatever_the_model_holds()
    {
        var order = new Order();
        var context = new EditContext(order);
        var litres = Draw(context, () => order.Litres);

        context.NoteUnread(litres, "Enter a whole number.");

        Assert.True(context.IsModified(litres));
    }

    [Fact]
    public void A_field_no_control_draws_is_modified_once_told_since_there_is_nothing_to_compare()
    {
        var order = new Order();
        var context = new EditContext(order);
        var name = new FieldIdentifier(order, nameof(Order.Name));

        context.NotifyFieldChanged(name);

        Assert.True(context.IsModified(name));
    }

    [Fact]
    public void Reset_puts_every_drawn_field_back_and_forgets_touched_modified_and_messages()
    {
        var order = new Order();
        var context = new EditContext(order);
        var name = Draw(context, () => order.Name);
        var litres = Draw(context, () => order.Litres);
        var city = Draw(context, () => order.Address.City);
        Type(context, () => order.Name, "Barrel");
        Type(context, () => order.Litres, 99);
        Type(context, () => order.Address.City, "Győr");
        context.NotifyFieldTouched(name);
        context.AddValidationMessage(litres, "Too much.");

        context.Reset();

        Assert.Equal(("Tank", 40, "Pécs"), (order.Name, order.Litres, order.Address.City));
        Assert.False(context.IsModifiedAny);
        Assert.False(context.IsTouched(name));
        Assert.False(context.IsModified(city));
        Assert.False(context.HasValidationMessages());
    }

    [Fact]
    public void Reset_refills_a_list_in_place_and_replaces_an_array_with_a_copy()
    {
        var order = new Order();
        var list = order.Tags;
        var context = new EditContext(order);
        var tags = Draw(context, () => order.Tags);
        var codes = Draw(context, () => order.Codes);
        order.Tags.Add("blue");
        context.NotifyFieldChanged(tags);
        order.Codes = ["A", "B"];
        context.NotifyFieldChanged(codes);

        context.Reset();

        Assert.Same(list, order.Tags);
        Assert.Equal(new[] { "red" }, order.Tags);
        Assert.Equal(new[] { "A" }, order.Codes);
        Assert.False(context.IsModifiedAny);
    }

    [Fact]
    public void Reset_leaves_a_set_as_it_is_since_it_cannot_be_refilled_without_its_type()
    {
        var order = new Order();
        var context = new EditContext(order);
        var marks = Draw(context, () => order.Marks);
        order.Marks.Add("y");
        context.NotifyFieldChanged(marks);

        context.Reset();

        Assert.Equal(2, order.Marks.Count);
        Assert.False(context.IsModified(marks));
    }

    [Fact]
    public void Reset_after_a_refused_save_clears_the_refusal_and_the_unread_text()
    {
        var order = new Order();
        var context = new EditContext(order);
        var litres = Draw(context, () => order.Litres);
        context.NoteUnread(litres, "Enter a whole number.");
        context.TouchAllRegisteredFields();
        context.NoteRefused();

        context.Reset();

        Assert.Empty(context.GetValidationMessages(litres));
        Assert.False(context.IsUnread(litres));
        Assert.False(context.IsModifiedAny);
    }

    [Fact]
    public void After_a_reset_the_original_is_still_the_first_value_drawn()
    {
        var order = new Order();
        var context = new EditContext(order);
        Draw(context, () => order.Name);
        Type(context, () => order.Name, "Barrel");
        context.Reset();

        Draw(context, () => order.Name);
        Type(context, () => order.Name, "Barrel");
        context.Reset();

        Assert.Equal("Tank", order.Name);
    }

    [Fact]
    public void MarkSaved_makes_what_each_field_holds_its_original_and_puts_nothing_back()
    {
        var order = new Order();
        var context = new EditContext(order);
        var name = Draw(context, () => order.Name);
        Type(context, () => order.Name, "Barrel");
        context.AddValidationMessage(name, "Taken.");

        context.MarkSaved();
        var afterSaving = context.IsModifiedAny;
        context.Reset();

        Assert.False(afterSaving);
        Assert.Equal("Barrel", order.Name);
    }

    [Fact]
    public void MarkSaved_keeps_the_messages_and_Reset_takes_them()
    {
        var order = new Order();
        var context = new EditContext(order);
        var name = Draw(context, () => order.Name);
        context.AddValidationMessage(name, "Taken.");

        context.MarkSaved();
        var kept = context.GetValidationMessages(name).Count;
        context.Reset();

        Assert.Equal(1, kept);
        Assert.Empty(context.GetValidationMessages(name));
    }

    [Fact]
    public void Reset_and_MarkSaved_each_change_the_count_the_form_writes_for_the_browser()
    {
        var order = new Order();
        var context = new EditContext(order);

        context.Reset();
        context.MarkSaved();

        Assert.Equal(2, context.Settled);
    }
}
