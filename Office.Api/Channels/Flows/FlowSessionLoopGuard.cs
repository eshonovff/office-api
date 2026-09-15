namespace Office.Api.Channels.Flows;

/// <summary>Ҳимоя аз ҳалқаи беохир дар граф — спека: "ҳадди аксар 50 қадам дар як сессия, баъд failed".</summary>
public static class FlowSessionLoopGuard
{
    public const int MaxSteps = 50;

    public static bool ShouldStop(int stepCount) => stepCount >= MaxSteps;
}
