namespace Office.Api.Features.Tasks;

/// <summary>
/// Pure planning барои PATCH /api/tasks/{id}/move: аз ID-ҳои дархост (beforeTaskId/afterTaskId)
/// ба position-ҳои PositionCalculator ва (агар лозим) тартиби пурраи reindex мегузарад.
///
/// beforeTaskId — ID-и таске, ки таски кӯчонидашуда бояд ПЕШ АЗ он биистад (яъне худи таск
/// SUCCESSOR-и он мешавад). afterTaskId — ID-и таске, ки таски кӯчонидашуда бояд БАЪД АЗ он
/// биистад (PREDECESSOR). Ин номгузорӣ БАРЪАКС аст ба параметрҳои PositionCalculator.Calculate
/// (beforePosition=predecessor, afterPosition=successor) — маҳз ҳамин номуфоиқат сабаби
/// bug-и position-swap буд (ниг. docs/bug-move-task-position-swap.md).
/// </summary>
public static class TaskMovePlanner
{
    public readonly record struct Sibling(Guid Id, double Position);

    public enum PlanStatus
    {
        Ok,
        PredecessorNotFound,
        SuccessorNotFound,
    }

    public readonly record struct MovePlan(
        PlanStatus Status,
        double NewPosition,
        bool NeedsReindex,
        IReadOnlyList<Guid> ReindexedOrder)
    {
        public static MovePlan NotFound(PlanStatus status) => new(status, 0, false, []);
    }

    public static MovePlan Plan(
        Guid movingTaskId,
        IReadOnlyList<Sibling> siblingsOrderedByPosition,
        Guid? beforeTaskId,
        Guid? afterTaskId)
    {
        double? predecessorPosition = null;
        double? successorPosition = null;
        var insertIndex = 0;

        if (afterTaskId is not null)
        {
            var idx = IndexOf(siblingsOrderedByPosition, afterTaskId.Value);
            if (idx < 0)
                return MovePlan.NotFound(PlanStatus.PredecessorNotFound);

            predecessorPosition = siblingsOrderedByPosition[idx].Position;
            insertIndex = idx + 1;
        }

        if (beforeTaskId is not null)
        {
            var idx = IndexOf(siblingsOrderedByPosition, beforeTaskId.Value);
            if (idx < 0)
                return MovePlan.NotFound(PlanStatus.SuccessorNotFound);

            successorPosition = siblingsOrderedByPosition[idx].Position;
        }

        var newPosition = PositionCalculator.Calculate(predecessorPosition, successorPosition);

        if (!PositionCalculator.NeedsReindex(predecessorPosition, successorPosition, newPosition))
            return new MovePlan(PlanStatus.Ok, newPosition, NeedsReindex: false, ReindexedOrder: []);

        var order = siblingsOrderedByPosition.Select(s => s.Id).ToList();
        order.Insert(insertIndex, movingTaskId);

        return new MovePlan(PlanStatus.Ok, newPosition, NeedsReindex: true, ReindexedOrder: order);
    }

    private static int IndexOf(IReadOnlyList<Sibling> siblings, Guid id)
    {
        for (var i = 0; i < siblings.Count; i++)
        {
            if (siblings[i].Id == id)
                return i;
        }

        return -1;
    }
}
