namespace Rask.Server;

/// <summary>Why a resume record was refused. Rides the <c>rask.sessions.resume_rejected</c> metric as a tag.</summary>
internal enum ResumeRejection
{
    None,
    Malformed,
    Unprotect,
    Principal,
    TooLarge,
    AtCapacity
}
