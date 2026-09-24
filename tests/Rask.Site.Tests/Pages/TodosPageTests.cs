using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Rask.Core.Routing;
using Rask.Site.Features;
using Rask.Site.Tests.Infrastructure;

namespace Rask.Site.Tests.Pages;

public sealed class TodosPageTests
{
    [Fact]
    public void The_todos_list_route_renders_the_seeded_items()
    {
        var routeState = new RouteState { Path = global::Rask.Site.Features.Routes.TodosPage() };

        var html = Page.Render(new global::Rask.Site.App(), TestServices.Default(routeState: routeState)).Html;

        Assert.Contains("Read the Rask README", html);
        Assert.Contains("Wire up a feature toggle", html);
        Assert.Contains(">New todo<", html);
    }

    [Fact]
    public void The_new_todo_route_opens_the_dialog_in_add_mode()
    {
        var routeState = new RouteState { Path = global::Rask.Site.Features.Routes.TodosPage() + "/new" };

        var html = Page.Render(new global::Rask.Site.App(), TestServices.Default(routeState: routeState)).Html;

        Assert.Contains(">Add todo<", html);
        Assert.Contains("todo-title", html);
    }

    [Fact]
    public void The_page_is_adding_when_the_path_ends_with_slash_new()
    {
        Assert.True(InvokeIsAdding("/todos/new"));
        Assert.True(InvokeIsAdding("/TODOS/NEW"));
        Assert.False(InvokeIsAdding(global::Rask.Site.Features.Routes.TodosPage()));
        Assert.False(InvokeIsAdding("/todos/abc/edit"));
    }

    [Fact]
    public void The_editing_item_is_found_by_the_route_param_id()
    {
        var routeState = new RouteState { Path = global::Rask.Site.Features.Routes.TodosPage() };
        var nav = new Navigator(routeState);
        var page = new TodosPage(nav, routeState);
        var todos = GetPrivateList(page);
        var firstId = todos[0].Id;
        var idField = typeof(TodosPage).GetProperty("Id")!;

        idField.SetValue(page, firstId);

        var editingItem = InvokeProperty<TodoItem?>(page, "EditingItem");
        Assert.NotNull(editingItem);
        Assert.Equal(firstId, editingItem!.Id);
    }

    [Fact]
    public void The_editing_item_is_null_for_an_unknown_id()
    {
        var routeState = new RouteState { Path = global::Rask.Site.Features.Routes.TodosPage() };
        var nav = new Navigator(routeState);
        var page = new TodosPage(nav, routeState);
        var idField = typeof(TodosPage).GetProperty("Id")!;

        idField.SetValue(page, Guid.NewGuid());

        var editingItem = InvokeProperty<TodoItem?>(page, "EditingItem");
        Assert.Null(editingItem);
    }

    [Fact]
    public void The_dialog_shows_when_adding_or_when_an_editing_item_matches()
    {
        var routeStateAdd = new RouteState { Path = global::Rask.Site.Features.Routes.TodosPage() + "/new" };
        var pageAdd = new TodosPage(new Navigator(routeStateAdd), routeStateAdd);

        Assert.True(InvokeProperty<bool>(pageAdd, "ShowDialog"));

        var routeStateList = new RouteState { Path = global::Rask.Site.Features.Routes.TodosPage() };
        var pageList = new TodosPage(new Navigator(routeStateList), routeStateList);

        Assert.False(InvokeProperty<bool>(pageList, "ShowDialog"));
    }

    [Fact]
    public void Saving_while_adding_appends_a_new_todo()
    {
        var routeState = new RouteState { Path = global::Rask.Site.Features.Routes.TodosPage() + "/new" };
        var nav = new Navigator(routeState);
        var page = new TodosPage(nav, routeState);
        var todos = GetPrivateList(page);
        var originalCount = todos.Count;
        var save = typeof(TodosPage).GetMethod("Save",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        TestNavigator.RunHandler(nav, () =>
            save.Invoke(page, [new TodoForm { Title = " New item " }]));

        Assert.Equal(originalCount + 1, todos.Count);
        Assert.Equal("New item", todos[^1].Title); // trimmed
    }

    [Fact]
    public void Saving_while_editing_mutates_the_existing_title()
    {
        var routeState = new RouteState { Path = global::Rask.Site.Features.Routes.TodosPage() };
        var nav = new Navigator(routeState);
        var page = new TodosPage(nav, routeState);
        var todos = GetPrivateList(page);
        var target = todos[0];
        var originalCount = todos.Count;

        // Put page in edit mode for this item.
        typeof(TodosPage).GetProperty("Id")!.SetValue(page, target.Id);
        routeState.Path = $"/todos/{target.Id}/edit";
        var save = typeof(TodosPage).GetMethod("Save",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        TestNavigator.RunHandler(nav, () =>
            save.Invoke(page, [new TodoForm { Title = "Updated title" }]));

        Assert.Equal(originalCount, todos.Count);
        Assert.Equal("Updated title", target.Title);
    }

    [Fact]
    public void Deleting_removes_the_item()
    {
        var routeState = new RouteState { Path = global::Rask.Site.Features.Routes.TodosPage() };
        var page = new TodosPage(new Navigator(routeState), routeState);
        var todos = GetPrivateList(page);
        var victim = todos[0];
        var delete = typeof(TodosPage).GetMethod("Delete",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        delete.Invoke(page, [victim]);

        Assert.DoesNotContain(victim, todos);
    }

    [Fact]
    public void An_update_syncs_the_form_title_from_the_editing_item()
    {
        var routeState = new RouteState { Path = global::Rask.Site.Features.Routes.TodosPage() };
        var page = new TodosPage(new Navigator(routeState), routeState);
        var todos = GetPrivateList(page);
        var target = todos[1];

        // Put page in edit mode for the second todo.
        typeof(TodosPage).GetProperty("Id")!.SetValue(page, target.Id);
        var onPropsChanged = typeof(TodosPage).GetMethod("OnUpdated",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        onPropsChanged.Invoke(page, null);

        var form = typeof(TodosPage).GetField("_form",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page) as TodoForm;
        Assert.NotNull(form);
        Assert.Equal(target.Title, form!.Title);
    }

    [Fact]
    public void A_TodoForm_with_an_empty_title_fails_Required()
    {
        var instance = new TodoForm();
        var ctx = new ValidationContext(instance);
        var results = new List<ValidationResult>();

        Validator.TryValidateObject(
            instance, ctx, results, true);

        Assert.NotEmpty(results);
    }

    private static bool InvokeIsAdding(string path)
    {
        var routeState = new RouteState { Path = path };
        var page = new TodosPage(new Navigator(routeState), routeState);
        return InvokeProperty<bool>(page, "IsAdding");
    }

    private static T InvokeProperty<T>(TodosPage page, string name)
    {
        var prop = typeof(TodosPage).GetProperty(name,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        return (T)prop!.GetValue(page)!;
    }

    private static List<TodoItem> GetPrivateList(TodosPage page)
    {
        var field = typeof(TodosPage).GetField("_todos",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (List<TodoItem>)field.GetValue(page)!;
    }
}
