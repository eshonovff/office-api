using Office.Api.Features.Tasks;
using Sibling = Office.Api.Features.Tasks.TaskMovePlanner.Sibling;

namespace Office.Api.Tests.Tasks;

public class TaskMovePlannerTests
{
    private static readonly Guid Moving = Guid.NewGuid();
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();

    [Fact]
    public void Plan_EmptyColumn_ReturnsStepNoReindex()
    {
        var plan = TaskMovePlanner.Plan(Moving, [], beforeTaskId: null, afterTaskId: null);

        Assert.Equal(TaskMovePlanner.PlanStatus.Ok, plan.Status);
        Assert.Equal(PositionCalculator.Step, plan.NewPosition);
        Assert.False(plan.NeedsReindex);
    }

    [Fact]
    public void Plan_MoveToFirst_BeforeGivenAfterNull_ReturnsHalfOfSuccessor()
    {
        // B(2000) мехоҳад пеш аз A(1000) биистад — "beforeTaskId=A" маънояш SUCCESSOR аст, на predecessor.
        Sibling[] siblings = [new(A, 1000)];

        var plan = TaskMovePlanner.Plan(Moving, siblings, beforeTaskId: A, afterTaskId: null);

        Assert.Equal(TaskMovePlanner.PlanStatus.Ok, plan.Status);
        Assert.Equal(500, plan.NewPosition);
        Assert.False(plan.NeedsReindex);
    }

    [Fact]
    public void Plan_MoveToLast_AfterGivenBeforeNull_ReturnsPredecessorPlusStep()
    {
        // A мехоҳад баъд аз B(2000) биистад — "afterTaskId=B" маънояш PREDECESSOR аст.
        Sibling[] siblings = [new(B, 2000)];

        var plan = TaskMovePlanner.Plan(Moving, siblings, beforeTaskId: null, afterTaskId: B);

        Assert.Equal(TaskMovePlanner.PlanStatus.Ok, plan.Status);
        Assert.Equal(3000, plan.NewPosition);
        Assert.False(plan.NeedsReindex);
    }

    [Fact]
    public void Plan_SwapAdjacentPair_BothEdgeMovesComputeCorrectly()
    {
        // Колонкаи ду-таска: A(1000), B(2000). B ба сар (пеш аз A), баъд A ба охир (баъд аз B-и нав).
        // Ин дақиқан ҳамон ду ҳолати "яктои null" аст, ки санҷиши "миён" (ҳарду дода шуда) пинҳон карда буд.
        var toFront = TaskMovePlanner.Plan(B, [new Sibling(A, 1000)], beforeTaskId: A, afterTaskId: null);
        Assert.Equal(500, toFront.NewPosition);

        var toBack = TaskMovePlanner.Plan(A, [new Sibling(B, 500)], beforeTaskId: null, afterTaskId: B);
        Assert.Equal(1500, toBack.NewPosition);

        // Пас аз ҳарду ҳаракат тартиб дар воқеъ иваз шуд: B(500) < A(1500).
        Assert.True(toFront.NewPosition < toBack.NewPosition);
    }

    [Fact]
    public void Plan_MoveToMiddle_BothGiven_ReturnsAverage()
    {
        Sibling[] siblings = [new(A, 1000), new(B, 2000)];

        var plan = TaskMovePlanner.Plan(Moving, siblings, beforeTaskId: B, afterTaskId: A);

        Assert.Equal(TaskMovePlanner.PlanStatus.Ok, plan.Status);
        Assert.Equal(1500, plan.NewPosition);
        Assert.False(plan.NeedsReindex);
    }

    [Fact]
    public void Plan_AcrossColumns_MovingTaskAbsentFromTargetSiblings_ComputesNormally()
    {
        // Кӯчонидан ба колонкаи дигар: siblings аз колонкаи ҳадаф меоянд, таски кӯчонидашуда
        // дар байни онҳо нест (чун дар манбаъ буд). Планнер аз колонка бехабар аст — рафтораш якхела.
        var movingFromOtherColumn = Guid.NewGuid();
        Sibling[] targetSiblings = [new(A, 1000), new(B, 2000)];

        var plan = TaskMovePlanner.Plan(movingFromOtherColumn, targetSiblings, beforeTaskId: B, afterTaskId: A);

        Assert.Equal(TaskMovePlanner.PlanStatus.Ok, plan.Status);
        Assert.Equal(1500, plan.NewPosition);
    }

    [Fact]
    public void Plan_NoOpMoveToSamePosition_ReturnsUnchangedPosition()
    {
        // Таск аллакай дар байни A ва B аст; дархости "кӯчонидан" ба ҳамон ҷой бояд ҳамон
        // position-ро баргардонад, на хато диҳад.
        Sibling[] siblings = [new(A, 1000), new(B, 2000)];

        var plan = TaskMovePlanner.Plan(Moving, siblings, beforeTaskId: B, afterTaskId: A);

        Assert.Equal(TaskMovePlanner.PlanStatus.Ok, plan.Status);
        Assert.Equal(1500, plan.NewPosition);
        Assert.False(plan.NeedsReindex);
    }

    [Fact]
    public void Plan_GapTooSmall_TriggersReindexWithMovingTaskInCorrectSlot()
    {
        Sibling[] siblings = [new(A, 1000.00001), new(B, 1000.00002)];

        var plan = TaskMovePlanner.Plan(Moving, siblings, beforeTaskId: B, afterTaskId: A);

        Assert.Equal(TaskMovePlanner.PlanStatus.Ok, plan.Status);
        Assert.True(plan.NeedsReindex);
        Assert.Equal([A, Moving, B], plan.ReindexedOrder);
    }

    [Fact]
    public void Plan_ReindexAtFront_PlacesMovingTaskBeforeAllSiblings()
    {
        Sibling[] siblings = [new(A, 0.00001), new(B, 0.00002)];

        var plan = TaskMovePlanner.Plan(Moving, siblings, beforeTaskId: A, afterTaskId: null);

        Assert.True(plan.NeedsReindex);
        Assert.Equal([Moving, A, B], plan.ReindexedOrder);
    }

    [Fact]
    public void Plan_PredecessorNotFound_ReturnsPredecessorNotFound()
    {
        var plan = TaskMovePlanner.Plan(Moving, [new Sibling(A, 1000)], beforeTaskId: null, afterTaskId: Guid.NewGuid());

        Assert.Equal(TaskMovePlanner.PlanStatus.PredecessorNotFound, plan.Status);
        Assert.False(plan.NeedsReindex);
    }

    [Fact]
    public void Plan_SuccessorNotFound_ReturnsSuccessorNotFound()
    {
        var plan = TaskMovePlanner.Plan(Moving, [new Sibling(A, 1000)], beforeTaskId: Guid.NewGuid(), afterTaskId: null);

        Assert.Equal(TaskMovePlanner.PlanStatus.SuccessorNotFound, plan.Status);
    }
}
