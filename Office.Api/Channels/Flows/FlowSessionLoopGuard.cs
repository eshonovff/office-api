namespace Office.Api.Channels.Flows;

/// <summary>
/// Ҳимоя аз ҳалқаи беохир дар граф (масалан Шарт↔Амал бе goto_flow, ки cycle-guard-и
/// StartAsync намебинад — он танҳо занҷири goto_flow-ро пайгирӣ мекунад, на ҳалқаи дохили
/// ҳамин flow). 200 (аз 50-и аввалини спека зиёд шуд, 2026-09-17): 50 барои сурвейи 15+
/// саволӣ (ҳар савол ~3 қадам — паём+амали гирифтани ҷавоб) кофӣ набуд.
/// </summary>
public static class FlowSessionLoopGuard
{
    public const int MaxSteps = 200;

    public static bool ShouldStop(int stepCount) => stepCount >= MaxSteps;
}
