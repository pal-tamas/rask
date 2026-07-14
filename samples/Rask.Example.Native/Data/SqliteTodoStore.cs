using Microsoft.Data.Sqlite;
using Rask.Example.Shared.Features;
using Rask.SQLite;

namespace Rask.Example.Native.Data;

// On-device SQLite persistence for the shared Todos screen. Uses Rask.SQLite's raw connection factory
// (Microsoft.Data.Sqlite, reflection-free — safe under iOS full-AOT), so the production pragmas (WAL,
// foreign_keys, busy_timeout) are applied on every connection. The database lives in the app sandbox, so
// the todos survive an app restart — unlike the in-memory store the Server/WASM showcase uses.
internal sealed class SqliteTodoStore : ITodoStore
{
    private readonly IRaskSqliteConnectionFactory _factory;

    public SqliteTodoStore(IRaskSqliteConnectionFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;

        using var connection = _factory.CreateOpen();
        Execute(connection, "CREATE TABLE IF NOT EXISTS todos(id TEXT PRIMARY KEY, title TEXT NOT NULL, completed INTEGER NOT NULL);");
    }

    public IReadOnlyList<TodoItem> GetAll()
    {
        using var connection = _factory.CreateOpen();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, title, completed FROM todos ORDER BY rowid;";

        var items = new List<TodoItem>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            items.Add(new TodoItem
            {
                Id = Guid.Parse(reader.GetString(0)),
                Title = reader.GetString(1),
                Completed = reader.GetInt64(2) != 0,
            });
        }

        return items;
    }

    public void Add(TodoItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        using var connection = _factory.CreateOpen();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO todos(id, title, completed) VALUES($id, $title, $completed);";
        command.Parameters.AddWithValue("$id", item.Id.ToString());
        command.Parameters.AddWithValue("$title", item.Title);
        command.Parameters.AddWithValue("$completed", item.Completed ? 1 : 0);
        command.ExecuteNonQuery();
    }

    public void Update(TodoItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        using var connection = _factory.CreateOpen();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE todos SET title = $title, completed = $completed WHERE id = $id;";
        command.Parameters.AddWithValue("$id", item.Id.ToString());
        command.Parameters.AddWithValue("$title", item.Title);
        command.Parameters.AddWithValue("$completed", item.Completed ? 1 : 0);
        command.ExecuteNonQuery();
    }

    public void Delete(Guid id)
    {
        using var connection = _factory.CreateOpen();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM todos WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        command.ExecuteNonQuery();
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
