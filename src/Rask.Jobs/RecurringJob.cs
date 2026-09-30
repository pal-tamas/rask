namespace Rask.Background;

/// <summary>A job on a schedule, still being worded: the cadence comes next.</summary>
/// <remarks>
/// <code>
/// o.Run&lt;PurgeStaleCarts&gt;().Every(1.Hour);
/// o.Run&lt;Backup&gt;().Daily.At(3, 0);
/// o.Run&lt;Digest&gt;().Weekly.On(DayOfWeek.Monday).At(9, 0);
/// o.Run&lt;CloseBooks&gt;().Monthly.On(1).At(6, 0);
/// </code>
/// <para>
/// The durable name defaults to the job's type name; <see cref="Named"/> overrides it. It is what the
/// <see cref="RecurringJobState"/> row is keyed by, so renaming the type — or the name — re-arms the job
/// as if it had never run.
/// </para>
/// </remarks>
public sealed class RecurringJob
{
    private readonly List<RecurringJob> _siblings;

    internal RecurringJob(string name, Func<IJob> factory, List<RecurringJob> siblings)
    {
        Name = name;
        Factory = factory;
        _siblings = siblings;
    }

    /// <summary>The durable name this job's last run is recorded under.</summary>
    public string Name { get; private set; }

    /// <summary>When it is due. Null until a cadence step is taken, which the host refuses to start without.</summary>
    public Schedule? Schedule { get; private set; }

    /// <summary>Builds the job to enqueue on each tick. Call it to run one off-schedule.</summary>
    public Func<IJob> Factory { get; }

    /// <summary>Runs it every <paramref name="interval"/>, measured from its last run.</summary>
    public RecurringJob Every(TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), interval, "A recurring interval must be positive.");
        }

        return With(new EverySchedule(interval));
    }

    /// <summary>Runs it once a day, at the time the next step gives.</summary>
    public DailyJob Daily => new(this);

    /// <summary>Runs it once a week, on the day the next step gives.</summary>
    public WeeklyJob Weekly => new(this);

    /// <summary>Runs it once a month, on the date the next step gives.</summary>
    public MonthlyJob Monthly => new(this);

    /// <summary>Records its runs under <paramref name="name"/> instead of the job's type name.</summary>
    public RecurringJob Named(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        RefuseADuplicateName(name);
        Name = name;
        return this;
    }

    /// <summary>
    /// Refuses a name another schedule already answers to. Two schedules sharing a name share one
    /// <see cref="RecurringJobState"/> row, so each would keep marking the other as having just run, and
    /// between them they would fire once — the failure that looks like "the second one never runs".
    /// </summary>
    internal void RefuseADuplicateName(string name)
    {
        if (_siblings.Any(r => !ReferenceEquals(r, this) && string.Equals(r.Name, name, StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                $"'{name}' is already scheduled. Two schedules for one job need distinct names: "
                + $"give one of them .Named(\"{name.ToLowerInvariant()}-nightly\").",
                nameof(name));
        }
    }

    internal RecurringJob With(Schedule schedule)
    {
        Schedule = schedule;
        return this;
    }
}
