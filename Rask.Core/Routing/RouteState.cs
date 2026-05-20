namespace Rask.Core.Routing;

public sealed class RouteState
{
    private string _path = "/";
    private IQueryCollection _query = QueryCollection.Empty;

    public string Path
    {
        get => _path;
        set
        {
            if (_path == value)
            {
                return;
            }

            _path = value;
            Changed?.Invoke();
        }
    }

    public IQueryCollection Query
    {
        get => _query;
        set
        {
            if (ReferenceEquals(_query, value))
            {
                return;
            }

            _query = value;
            Changed?.Invoke();
        }
    }

    public event Action? Changed;
}
