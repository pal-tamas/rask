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

    // What it last reported, numbered: the watch writes the row when the written number is behind, and says it on screen
    // when the said one is.
    private WorkReport _latest;
    private long _reports;
    private long _written;
    private long _said;
    private long _saidAt = long.MinValue;

    // How its handler said it ends (Jobs.Warn/Refuse/Note): the worst outcome said stands, with its words.
    private WorkState? _outcome;
    private string? _detail;

    public JobRun(Job job, TimeProvider clock, CancellationToken host)
        : this(job.Id, job.Type, job.Lane, job.Notice, job.UserId, job.TenantId, clock, host)
    {
        Shared = job.Shared;
        Batch = job.Batch;
    }

    /// <summary>A run of a job that has no row of its own — what the test fake runs.</summary>
    public JobRun(long id, string type, string? lane, string? notice, Guid? owner, Guid? tenant, TimeProvider clock, CancellationToken host)
    {
        Id = id;
        Type = type;
        Lane = lane;
        Notice = notice;
        Owner = owner;
        Tenant = tenant;
        _clock = clock;
        _source = CancellationTokenSource.CreateLinkedTokenSource(host);
        Started = clock.GetUtcNow().UtcDateTime;
        StartedTimestamp = clock.GetTimestamp();
        _lastProgress = Started.Ticks;
    }

    public long Id { get; }

    public string Type { get; }

    public string? Lane { get; }

    /// <summary>The title its enqueuer watches it under (<c>.Noticed(…)</c>), or null.</summary>
    public string? Notice { get; }

    /// <summary>The user it was enqueued for: the one who watches it.</summary>
    public Guid? Owner { get; }

    /// <summary>The tenant it was enqueued in.</summary>
    public Guid? Tenant { get; }

    /// <summary>Whether its tenant shares it (<c>.ByTenant()</c>).</summary>
    public bool Shared { get; init; }

    /// <summary>The batch it was enqueued in, or null.</summary>
    public Guid? Batch { get; init; }

    /// <summary>How its handler said it ends — Warned, or Failed for a refusal — or null when it said neither.</summary>
    public WorkState? Outcome
    {
        get
        {
            lock (_gate)
            {
                return _outcome;
            }
        }
    }

    /// <summary>What its handler said of how it ends, or null.</summary>
    public string? Detail
    {
        get
        {
            lock (_gate)
            {
                return _detail;
            }
        }
    }

    /// <summary>Whether its handler refused it (<c>Jobs.Refuse</c>): it ends failed for good, whatever it does next.</summary>
    public bool IsRefused => Outcome == WorkState.Failed;

    /// <summary>Whether its enqueuer watches it: it has a title, and somebody to show it to.</summary>
    public bool IsNoticed => Notice is not null && Owner is not null;

    /// <summary>
    ///     Where a report its enqueuer should see goes — at most once every <see cref="SayEvery" />; the watch says the latest
    ///     once a poll if a report came between. Null for a run nobody watches.
    /// </summary>
    public Action<WorkReported>? Say { get; set; }

    /// <summary>How often a report reaches the screen at most: about four times a second, whatever the job reports.</summary>
    public static TimeSpan SayEvery => TimeSpan.FromMilliseconds(250);

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

    /// <summary>What it last reported: its words and its count, each null when it said none.</summary>
    public WorkReport Latest
    {
        get
        {
            lock (_gate)
            {
                return _latest;
            }
        }
    }

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

    /// <summary>
    ///     It got on: what <c>Jobs.Report(…)</c> tells the job of the flow it runs on. Only the latest is kept; it is said on
    ///     screen at once unless it was said less than <see cref="SayEvery" /> ago, and then by the watch (<see cref="SayUnsaid" />).
    /// </summary>
    /// <remarks>
    ///     Said under the gate that <see cref="End" /> takes, and never once it has ended: so nothing this run says can land
    ///     after the end its processor says once it has saved the outcome.
    /// </remarks>
    public void Report(string? progress, int? done, int? of)
    {
        var now = _clock.GetTimestamp();
        lock (_gate)
        {
            _latest = new WorkReport(progress, done, of);
            _reports++;
            if (CanSay && (_saidAt == long.MinValue || _clock.GetElapsedTime(_saidAt, now) >= SayEvery))
            {
                SayLatest(now);
            }
        }

        Interlocked.Exchange(ref _lastProgress, _clock.GetUtcNow().UtcDateTime.Ticks);
    }

    /// <summary>
    ///     Its handler said how it ends: <paramref name="outcome" /> — Warned, Failed, or null for a note on a run that is done —
    ///     with <paramref name="detail" />. The worst said stands: a warning never lifts a refusal, and a note never lifts
    ///     either; at one outcome, the latest words are kept.
    /// </summary>
    public void Conclude(WorkState? outcome, string detail)
    {
        lock (_gate)
        {
            if (Severity(outcome) >= Severity(_outcome))
            {
                _outcome = outcome ?? _outcome;
                _detail = detail;
            }
        }
    }

    private static int Severity(WorkState? outcome) => outcome switch
    {
        WorkState.Failed => 2,
        WorkState.Warned => 1,
        _ => 0,
    };

    /// <summary>The latest report when the row has not been written with it yet — and marks it written. Null when it has.</summary>
    public WorkReport? Unwritten()
    {
        lock (_gate)
        {
            if (_written == _reports)
            {
                return null;
            }

            _written = _reports;
            return _latest;
        }
    }

    /// <summary>Says the latest report on screen when it has not been said yet: what the watch does once a poll.</summary>
    public void SayUnsaid()
    {
        lock (_gate)
        {
            if (CanSay && _said != _reports)
            {
                SayLatest(_clock.GetTimestamp());
            }
        }
    }

    // Caller holds _gate.
    private bool CanSay => Say is not null && !_ended;

    // Caller holds _gate.
    private void SayLatest(long now)
    {
        _said = _reports;
        _saidAt = now;
        Say!(ToWork(WorkState.Running, _latest));
    }

    /// <summary>The run as its enqueuer sees it, <paramref name="state" /> now, with <paramref name="report" />.</summary>
    public WorkReported ToWork(WorkState state, WorkReport report) =>
        new(Id, Notice ?? "", report.Progress, report.Done, report.Of, state, _clock.GetUtcNow().UtcDateTime)
        {
            Batch = Batch,
            Detail = state == WorkState.Running ? null : Detail,
            Owner = Owner,
            Tenant = Tenant,
            Lane = Lane,
            Shared = Shared,
            Kind = Type,
        };

    /// <summary>How it ended, once its handler is over: what its handler said, or <paramref name="otherwise" />.</summary>
    public WorkState Ending(WorkState otherwise) => otherwise == WorkState.Done && Outcome is { } said ? said : otherwise;

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
