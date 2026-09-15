using Hangfire;

namespace Office.Api.Channels.Flows;

/// <summary>Ҳамбастагии Hangfire барои action:delay — ниг. FlowEngine.ExecuteActionNodeAsync (Schedule).</summary>
[AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 300, 1800])]
public class FlowEngineJob(FlowEngine engine)
{
    public Task ResumeFromDelayAsync(Guid sessionId, CancellationToken ct) => engine.ResumeFromDelayAsync(sessionId, ct);
}
