using Autobahn.Domain.Common;

namespace Autobahn.Domain.Fleet;

public sealed class DownloadJob : IAggregateRoot
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public required string Repo { get; init; }
    public string Revision { get; init; } = "main";
    public string Source { get; init; } = "";
    public string RequestedBy { get; init; } = "";
    public DownloadState State { get; private set; }
    public double CreatedAt { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d;
    public double? StartedAt { get; private set; }
    public double? FinishedAt { get; private set; }
    public long? TotalBytes { get; private set; }
    public long DoneBytes { get; private set; }
    public int? Files { get; private set; }
    public double BytesPerSecond => StartedAt is { } t ? DoneBytes / Math.Max(1, Now - t) : 0;
    public string Sha { get; private set; } = "";
    public string Path { get; private set; } = "";
    public string ErrorCode { get; private set; } = "";
    public string Message { get; private set; } = "";
    private static double Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d;

    public void Start()
    {
        if (State != DownloadState.Queued) throw new DomainException("invalid_transition", "Only queued downloads can start.");
        State = DownloadState.Running; StartedAt = Now;
    }

    public void Describe(long? totalBytes, int files, string sha, string path)
    { TotalBytes = totalBytes; Files = files; Sha = sha; Path = path; }

    public void Progress(long bytes)
    {
        if (State != DownloadState.Running || bytes < DoneBytes || bytes < 0)
            throw new DomainException("invalid_progress", "Download progress must increase while running.");
        DoneBytes = bytes;
    }

    public void Finish(DownloadState state, string code = "", string message = "")
    {
        if (State is not (DownloadState.Queued or DownloadState.Running) || state is DownloadState.Queued or DownloadState.Running)
            throw new DomainException("invalid_transition", "The download has already finished.");
        State = state; FinishedAt = Now; ErrorCode = code; Message = message;
    }
}
