using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Rask.Core.Routing;
using Rask.Example.Shared.Features;
using Rask.Example.Shared.Tests.Infrastructure;

namespace Rask.Example.Shared.Tests.Pages;

public sealed class TodosPageTests
{
    [Fact]
    public void Route_TodosList_RendersSeededItems()
    {
        var routeState = new RouteState { Path = "/todos" };
        var html = new Shared.App()
            .RenderAsLiveRoot(TestServices.Default(routeState: routeState));

        Assert.Contains("Read the Rask README", html);
        Assert.Contains("Wire up a feature toggle", html);
        Assert.Contains(">New todo<", html);
    }

    [Fact]
    public void Route_TodosNew_OpensDialogInAddMode()
    {
        var routeState = new RouteState { Path = "/todos/new" };
        var html = new Shared.App()
            .RenderAsLiveRoot(TestServices.Default(routeState: routeState));

        Assert.Contains(">Add todo<", html);
        Assert.Contains("todo-title", html);
    }

    [Fact]
    public void IsAdding_True_WhenPathEndsWithSlashNew()
    {
        Assert.True(InvokeIsAdding("/todos/new"));
        Assert.True(InvokeIsAdding("/TODOS/NEW"));
        Assert.False(InvokeIsAdding("/todos"));
        Assert.False(InvokeIsAdding("/todos/abc/edit"));
    }

    [Fact]
    public void EditingItem_FindsItemByRouteParamId()
    {
        var routeState = new RouteState { Path = "/todos" };
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
    public void EditingItem_NullForUnknownId()
    {
        var routeState = new RouteState { Path = "/todos" };
        var nav = new Navigator(routeState);
        var page = new TodosPage(nav, routeState);

        var idField = typeof(TodosPage).GetProperty("Id")!;
        idField.SetValue(page, Guid.NewGuid());

        var editingItem = InvokeProperty<TodoItem?>(page, "EditingItem");
        Assert.Null(editingItem);
    }

    [Fact]
    public void ShowDialog_TrueWhenAdding_OrWhenEditingItemMatches()
    {
        var routeStateAdd = new RouteState { Path = "/todos/new" };
        var pageAdd = new TodosPage(new Navigator(routeStateAdd), routeStateAdd);
        Assert.True(InvokeProperty<bool>(pageAdd, "ShowDialog"));

        var routeStateList = new RouteState { Path = "/todos" };
        var pageList = new TodosPage(new Navigator(routeStateList), routeStateList);
        Assert.False(InvokeProperty<bool>(pageList, "ShowDialog"));
    }

    [Fact]
    public void Save_AppendsNewTodo_WhenAdding()
    {
        var routeState = new RouteState { Path = "/todos/new" };
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
    public void Save_MutatesExistingTitle_WhenEditing()
    {
        var routeState = new RouteState { Path = "/todos" };
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
    public void Delete_RemovesItem()
    {
        var routeState = new RouteState { Path = "/todos" };
        var page = new TodosPage(new Navigator(routeState), routeState);
        var todos = GetPrivateList(page);
        var victim = todos[0];

        var delete = typeof(TodosPage).GetMethod("Delete",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        delete.Invoke(page, [victim]);

        Assert.DoesNotContain(victim, todos);
    }

    [Fact]
    public void OnPropsChanged_SyncsFormTitleFromEditingItem()
    {
        var routeState = new RouteState { Path = "/todos" };
        var page = new TodosPage(new Navigator(routeState), routeState);
        var todos = GetPrivateList(page);
        var target = todos[1];

        // Put page in edit mode for the second todo.
        typeof(TodosPage).GetProperty("Id")!.SetValue(page, target.Id);

        var onPropsChanged = typeof(TodosPage).GetMethod("OnPropsChanged",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        onPropsChanged.Invoke(page, null);

        var form = typeof(TodosPage).GetField("_form",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page) as TodoForm;
        Assert.NotNull(form);
        Assert.Equal(target.Title, form!.Title);
    }

    [Fact]
    public void TodoForm_EmptyTitle_FailsRequired()
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
