namespace Rask.Background;

/// <summary>
///     One job in its handler on this host: the token its work runs under (<c>Current.Cancellation</c>), its limits, the last
///     progress it reported, and — once — why it was stopped. Stopping it cancels that token; what stopped it first is what it
///     says, and once its handler has returned nothing stops it any more.
/// </summary>
internal sealed class JobRun
{
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _source;
    private readonly TimeProvider _clock;
    private long _lastProgress;
    private JobStop? _stopped;
    private bool _ended;

    public JobRun(Job job, TimeProvider clock, CancellationToken host)
    {
        Id = job.Id;
        Type = job.Type;
        Lane = job.Lane;
        _clock = clock;
        _source = CancellationTokenSource.CreateLinkedTokenSource(host);
        Started = clock.GetUtcNow().UtcDateTime;
        StartedTimestamp = clock.GetTimestamp();
        _lastProgress = Started.Ticks;
    }

    public long Id { get; }

    public string Type { get; }

    public string? Lane { get; }

    /// <summary>When the handler started (UTC), on the processor's clock: the time limit and the idle watch count from here.</summary>
    public DateTime Started { get; }

    /// <summary>The same moment as a timestamp, for the durations the metrics and the drain report measure.</summary>
    public long StartedTimestamp { get; }

    /// <summary>The token the handler's work runs under: cancelled when the host's grace ends, or when it is stopped.</summary>
    public CancellationToken Token => _source.Token;

    /// <summary>The time limit of this run, if any.</summary>
    public TimeSpan? After { get; private set; }

    /// <summary>How long this run may report no progress, if it is watched for that.</summary>
    public TimeSpan? IdleFor { get; private set; }

    /// <summary>When it last reported progress (UTC) — <see cref="Started" /> until it reports any.</summary>
    public DateTime LastProgress => new(Interlocked.Read(ref _lastProgress), DateTimeKind.Utc);

    /// <summary>What it last reported, if anything.</summary>
    public string? LastReport { get; private set; }

    /// <summary>Why it was stopped, or null while it runs on.</summary>
    public JobStop? Stopped
    {
        get
        {
            lock (_gate)
            {
                return _stopped;
            }
        }
    }

    /// <summary>When it was stopped (UTC), or null.</summary>
    public DateTime? StoppedAt { get; private set; }

    /// <summary>When the watch last said it ignores its stop (UTC), so it says so once a minute and no more.</summary>
    public DateTime? WarnedAt { get; set; }

    /// <summary>Whether its handler has returned (or thrown): nothing stops it any more.</summary>
    public bool HasEnded
    {
        get
        {
            lock (_gate)
            {
                return _ended;
            }
        }
    }

    /// <summary>Takes the limits its job's type has, measured from <see cref="Started" />.</summary>
    public void Limit(TimeSpan? after, TimeSpan? idleFor)
    {
        After = after;
        IdleFor = idleFor;
    }

    /// <summary>It got on: what <c>Jobs.Report(…)</c> tells the job of the flow it runs on.</summary>
    public void Report(string? progress)
    {
        LastReport = progress;
        Interlocked.Exchange(ref _lastProgress, _clock.GetUtcNow().UtcDateTime.Ticks);
    }

    /// <summary>The limit it is past at <paramref name="now" />, if any: its time limit first, then the idle watch.</summary>
    public JobStop? LimitPassed(DateTime now)
    {
        if (After is { } after && now - Started >= after)
        {
            return JobStop.TimeLimit;
        }

        return IdleFor is { } idle && now - LastProgress >= idle ? JobStop.Idle : null;
    }

    /// <summary>
    ///     Stops it for <paramref name="why" />: cancels its token. False, changing nothing, when it was stopped already — the
    ///     first reason stands — or its handler has returned.
    /// </summary>
    public bool TryStop(JobStop why)
    {
        lock (_gate)
        {
            if (_ended || _stopped is not null)
            {
                return false;
            }

            _stopped = why;
            StoppedAt = _clock.GetUtcNow().UtcDateTime;
        }

        try
        {
            _source.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // It ended meanwhile: nothing is left to stop.
        }
        catch (AggregateException)
        {
            // A callback registered on the token threw; the token is cancelled all the same, and the handler's own
            // await reports what it does with that.
        }

        return true;
    }

    /// <summary>Its handler has returned (or thrown): from now on nothing stops it. Answers why it was stopped, if it was.</summary>
    public JobStop? End()
    {
        lock (_gate)
        {
            _ended = true;
            return _stopped;
        }
    }

    /// <summary>Lets the token go once the run is over.</summary>
    public void Release() => _source.Dispose();
}
