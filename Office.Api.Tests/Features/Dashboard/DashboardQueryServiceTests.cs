using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Office.Api.Auth;
using Office.Api.Common;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Features.Dashboard;

namespace Office.Api.Tests.Features.Dashboard;

public class DashboardQueryServiceTests
{
    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ClaimsPrincipal MakePrincipal(Guid userId, params string[] roles)
    {
        var claims = new List<Claim> { new(JwtRegisteredClaimNames.Sub, userId.ToString()) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static User SeedUser(AppDbContext db, bool onlyAssigned = false)
    {
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            FullName = "Test User",
            Username = $"user-{Guid.NewGuid():N}",
            PasswordHash = "x",
            OnlyAssigned = onlyAssigned,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Users.Add(user);
        return user;
    }

    private static Channel SeedChannel(AppDbContext db, ChannelType type = ChannelType.WhatsApp)
    {
        var channel = new Channel
        {
            Id = Guid.CreateVersion7(),
            Type = type,
            Name = "Test Channel",
            ExternalId = Guid.NewGuid().ToString(),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Channels.Add(channel);
        return channel;
    }

    private static Conversation SeedConversation(
        AppDbContext db, Channel channel, ConversationStatus status = ConversationStatus.New,
        Guid? assignedTo = null, DateTimeOffset? windowExpiresAt = null)
    {
        var conversation = new Conversation
        {
            Id = Guid.CreateVersion7(),
            ChannelId = channel.Id,
            Channel = channel,
            ExternalId = Guid.NewGuid().ToString(),
            Status = status,
            AssignedTo = assignedTo,
            WindowExpiresAt = windowExpiresAt,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Conversations.Add(conversation);
        return conversation;
    }

    [Fact]
    public async Task ComputeAsync_EmptyDatabase_DoesNotThrowAndReturnsZeros()
    {
        var db = CreateDb();
        var owner = SeedUser(db);
        await db.SaveChangesAsync();

        var service = new DashboardQueryService(db, new ChannelAccessGuard(db));
        var result = await service.ComputeAsync(MakePrincipal(owner.Id, RoleKeys.Owner), CancellationToken.None);

        Assert.Equal(0, result.ActionRequired.ClosingWindows.Count);
        Assert.Empty(result.ActionRequired.ClosingWindows.Items);
        Assert.Equal(0, result.ActionRequired.Unassigned);
        Assert.Equal(0, result.ActionRequired.FailedMessages.Count);
        Assert.Empty(result.ActionRequired.FailedMessages.Groups);
        Assert.Equal(0, result.ActionRequired.ChannelIssues.Count);
        Assert.Empty(result.ActionRequired.ChannelIssues.Items);
        Assert.Equal(0, result.ActionRequired.OverdueTasks.Count);
        Assert.Empty(result.ActionRequired.OverdueTasks.Items);

        Assert.Empty(result.MyWork.MyConversations);
        Assert.Equal(0, result.MyWork.MyUnread);
        Assert.Equal(0, result.MyWork.MyTasksToday.Count);
        Assert.Empty(result.MyWork.MyTasksToday.Items);
        Assert.Equal(0, result.MyWork.MyTasksOverdue.Count);
        Assert.Empty(result.MyWork.MyTasksOverdue.Items);
    }

    [Fact]
    public async Task ComputeAsync_OnlyAssignedOperator_SeesOnlyOwnConversations()
    {
        var db = CreateDb();
        var operatorUser = SeedUser(db, onlyAssigned: true);
        var otherUser = SeedUser(db);
        var channel = SeedChannel(db);
        db.ChannelMembers.Add(new ChannelMember { ChannelId = channel.Id, UserId = operatorUser.Id });

        // Ба ҳеҷ кас вогузошта нашуда — only_assigned бояд онро низ намебинад (на танҳо
        // "ба дигаре вогузошта"-ро).
        SeedConversation(db, channel, status: ConversationStatus.New, assignedTo: null);
        var mine = SeedConversation(db, channel, status: ConversationStatus.InProgress, assignedTo: operatorUser.Id,
            windowExpiresAt: DateTimeOffset.UtcNow.AddHours(1));
        SeedConversation(db, channel, status: ConversationStatus.InProgress, assignedTo: otherUser.Id,
            windowExpiresAt: DateTimeOffset.UtcNow.AddHours(1));

        await db.SaveChangesAsync();

        var service = new DashboardQueryService(db, new ChannelAccessGuard(db));
        var result = await service.ComputeAsync(MakePrincipal(operatorUser.Id), CancellationToken.None);

        Assert.Equal(0, result.ActionRequired.Unassigned);
        Assert.Equal(1, result.ActionRequired.ClosingWindows.Count);
        Assert.Equal(mine.Id, result.ActionRequired.ClosingWindows.Items.Single().ConversationId);
    }

    [Fact]
    public async Task ComputeAsync_Owner_SeesEverythingAcrossAllChannels()
    {
        var db = CreateDb();
        var owner = SeedUser(db);
        var channelA = SeedChannel(db);
        var channelB = SeedChannel(db, ChannelType.Instagram);
        // Owner аъзои ягон канал нест — вале бояд ҳама якхела бубинад (CanSeeAllChannels).
        SeedConversation(db, channelA, status: ConversationStatus.New, assignedTo: null);
        SeedConversation(db, channelB, status: ConversationStatus.New, assignedTo: null);

        await db.SaveChangesAsync();

        var service = new DashboardQueryService(db, new ChannelAccessGuard(db));
        var result = await service.ComputeAsync(MakePrincipal(owner.Id, RoleKeys.Owner), CancellationToken.None);

        Assert.Equal(2, result.ActionRequired.Unassigned);
    }

    [Fact]
    public async Task ComputeAsync_ClosingWindows_ExcludesClosedConversationsAndFarFutureWindows()
    {
        var db = CreateDb();
        var owner = SeedUser(db);
        var channel = SeedChannel(db);

        SeedConversation(db, channel, status: ConversationStatus.Closed, windowExpiresAt: DateTimeOffset.UtcNow.AddMinutes(30));
        SeedConversation(db, channel, status: ConversationStatus.InProgress, windowExpiresAt: DateTimeOffset.UtcNow.AddHours(5));
        var closingSoon = SeedConversation(db, channel, status: ConversationStatus.InProgress, windowExpiresAt: DateTimeOffset.UtcNow.AddMinutes(90));

        await db.SaveChangesAsync();

        var service = new DashboardQueryService(db, new ChannelAccessGuard(db));
        var result = await service.ComputeAsync(MakePrincipal(owner.Id, RoleKeys.Owner), CancellationToken.None);

        Assert.Equal(1, result.ActionRequired.ClosingWindows.Count);
        Assert.Equal(closingSoon.Id, result.ActionRequired.ClosingWindows.Items.Single().ConversationId);
    }

    [Fact]
    public async Task ComputeAsync_FailedMessages_ExcludesStaleAndNonFailedMessages()
    {
        var db = CreateDb();
        var owner = SeedUser(db);
        var channel = SeedChannel(db);
        var conversation = SeedConversation(db, channel);

        db.Messages.Add(new Message
        {
            Id = Guid.CreateVersion7(), ConversationId = conversation.Id, Direction = MessageDirection.Outbound,
            Type = MessageType.Text, DeliveryStatus = MessageDeliveryStatus.Failed, FailureReason = "Timeout",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10), // берун аз 7 рӯз — набояд ба ҳисоб равад
        });
        db.Messages.Add(new Message // Sent, на Failed
        {
            Id = Guid.CreateVersion7(), ConversationId = conversation.Id, Direction = MessageDirection.Outbound,
            Type = MessageType.Text, DeliveryStatus = MessageDeliveryStatus.Sent,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync();

        var service = new DashboardQueryService(db, new ChannelAccessGuard(db));
        var result = await service.ComputeAsync(MakePrincipal(owner.Id, RoleKeys.Owner), CancellationToken.None);

        Assert.Equal(0, result.ActionRequired.FailedMessages.Count);
        Assert.Empty(result.ActionRequired.FailedMessages.Groups);
    }

    [Fact]
    public async Task ComputeAsync_FailedMessages_GroupsByFailureCodeNotByIndividualMessage()
    {
        var db = CreateDb();
        var owner = SeedUser(db);
        var channel = SeedChannel(db);
        var conversation = SeedConversation(db, channel);

        // fbtrace_id дар FailureDetail-и ҳар паём ягона аст — GROUP BY бар он бефоида буд.
        // FailureCode бошад такрор мешавад: ин ду паём бояд як гурӯҳ шаванд.
        db.Messages.Add(new Message
        {
            Id = Guid.CreateVersion7(), ConversationId = conversation.Id, Direction = MessageDirection.Outbound,
            Type = MessageType.Image, DeliveryStatus = MessageDeliveryStatus.Failed,
            FailureReason = "Meta файлро зеркашӣ карда натавонист.", FailureCode = "FB_100_2018074",
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
        });
        db.Messages.Add(new Message
        {
            Id = Guid.CreateVersion7(), ConversationId = conversation.Id, Direction = MessageDirection.Outbound,
            Type = MessageType.Audio, DeliveryStatus = MessageDeliveryStatus.Failed,
            FailureReason = "Meta файлро зеркашӣ карда натавонист.", FailureCode = "FB_100_2018074",
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-2),
        });
        db.Messages.Add(new Message
        {
            Id = Guid.CreateVersion7(), ConversationId = conversation.Id, Direction = MessageDirection.Outbound,
            Type = MessageType.Text, DeliveryStatus = MessageDeliveryStatus.Failed,
            FailureReason = "Хидмати Meta муваққатан дастрас нест.", FailureCode = "IG_2",
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-3),
        });
        db.Messages.Add(new Message // код бароварда нашуд (масалан хатои шабака, на GraphApiException)
        {
            Id = Guid.CreateVersion7(), ConversationId = conversation.Id, Direction = MessageDirection.Outbound,
            Type = MessageType.Text, DeliveryStatus = MessageDeliveryStatus.Failed,
            FailureReason = "Network timeout", FailureCode = null,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-4),
        });

        await db.SaveChangesAsync();

        var service = new DashboardQueryService(db, new ChannelAccessGuard(db));
        var result = await service.ComputeAsync(MakePrincipal(owner.Id, RoleKeys.Owner), CancellationToken.None);

        Assert.Equal(4, result.ActionRequired.FailedMessages.Count);
        Assert.Equal(3, result.ActionRequired.FailedMessages.Groups.Count); // FB_100_2018074, IG_2, null

        var fbGroup = result.ActionRequired.FailedMessages.Groups.Single(g => g.FailureCode == "FB_100_2018074");
        Assert.Equal(2, fbGroup.Count);

        var igGroup = result.ActionRequired.FailedMessages.Groups.Single(g => g.FailureCode == "IG_2");
        Assert.Equal(1, igGroup.Count);

        // А1 (2026-08-26): матни тайёр аз DTO бардошта шуд — фронт (tg/ru) худаш тарҷума
        // мекунад. failure_code=null низ дар DTO танҳо код (null) аст, на матни "Номаълум".
        var unknownGroup = result.ActionRequired.FailedMessages.Groups.Single(g => g.FailureCode == null);
        Assert.Equal(1, unknownGroup.Count);
    }

    [Fact]
    public async Task ComputeAsync_ChannelIssues_CountsChannelWithAnyProblem()
    {
        var db = CreateDb();
        var owner = SeedUser(db);
        SeedChannel(db); // соф
        var broken = SeedChannel(db);
        broken.RequiresReconnect = true;

        await db.SaveChangesAsync();

        var service = new DashboardQueryService(db, new ChannelAccessGuard(db));
        var result = await service.ComputeAsync(MakePrincipal(owner.Id, RoleKeys.Owner), CancellationToken.None);

        Assert.Equal(1, result.ActionRequired.ChannelIssues.Count);
        Assert.Equal(broken.Id, result.ActionRequired.ChannelIssues.Items.Single().ChannelId);
    }

    [Fact]
    public async Task ComputeAsync_ChannelIssues_InactiveChannelWithNoOtherProblem_IsNotCounted()
    {
        // А2 (2026-08-26): каналҳои қасдан хомӯшкардашуда (масалан тестӣ) мушкил нестанд —
        // is_active-и танҳо набояд channelIssues-ро "пур" кунад.
        var db = CreateDb();
        var owner = SeedUser(db);
        var inactive = SeedChannel(db);
        inactive.IsActive = false;

        await db.SaveChangesAsync();

        var service = new DashboardQueryService(db, new ChannelAccessGuard(db));
        var result = await service.ComputeAsync(MakePrincipal(owner.Id, RoleKeys.Owner), CancellationToken.None);

        Assert.Equal(0, result.ActionRequired.ChannelIssues.Count);
        Assert.Empty(result.ActionRequired.ChannelIssues.Items);
    }

    [Fact]
    public async Task ComputeAsync_OverdueTasks_ExcludesDoneColumnAndFutureDueDates()
    {
        var db = CreateDb();
        var owner = SeedUser(db);
        var project = new Project { Id = Guid.CreateVersion7(), Name = "P", Key = "P", CreatedAt = DateTimeOffset.UtcNow };
        var todoColumn = new BoardColumn { Id = Guid.CreateVersion7(), ProjectId = project.Id, Name = "Todo", OrderIndex = 0, IsDoneColumn = false };
        var doneColumn = new BoardColumn { Id = Guid.CreateVersion7(), ProjectId = project.Id, Name = "Done", OrderIndex = 1, IsDoneColumn = true };
        db.Projects.Add(project);
        db.BoardColumns.AddRange(todoColumn, doneColumn);

        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

        db.Tasks.Add(new TaskItem
        {
            Id = Guid.CreateVersion7(), ProjectId = project.Id, ColumnId = todoColumn.Id, Title = "Overdue",
            DueDate = yesterday, CreatedBy = owner.Id, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });
        db.Tasks.Add(new TaskItem
        {
            Id = Guid.CreateVersion7(), ProjectId = project.Id, ColumnId = doneColumn.Id, Title = "Overdue but in done column",
            DueDate = yesterday, CreatedBy = owner.Id, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });
        db.Tasks.Add(new TaskItem
        {
            Id = Guid.CreateVersion7(), ProjectId = project.Id, ColumnId = todoColumn.Id, Title = "Not due yet",
            DueDate = tomorrow, CreatedBy = owner.Id, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync();

        var service = new DashboardQueryService(db, new ChannelAccessGuard(db));
        var result = await service.ComputeAsync(MakePrincipal(owner.Id, RoleKeys.Owner), CancellationToken.None);

        Assert.Equal(1, result.ActionRequired.OverdueTasks.Count);
        // ProjectId — фронт бе он линк ба тахтаи дуруст (/projects/{id}?task={taskId}) сохта
        // наметавонад (ниг. TaskPreviewItem).
        var item = Assert.Single(result.ActionRequired.OverdueTasks.Items);
        Assert.Equal(project.Id, item.ProjectId);
        Assert.Equal("Overdue", item.Title);
    }

    [Fact]
    public async Task ComputeAsync_OverdueTasks_NonMemberOfProject_DoesNotSeeIt()
    {
        var db = CreateDb();
        var operatorUser = SeedUser(db); // на Owner, на аъзои project
        var project = new Project { Id = Guid.CreateVersion7(), Name = "P", Key = "P", CreatedAt = DateTimeOffset.UtcNow };
        var column = new BoardColumn { Id = Guid.CreateVersion7(), ProjectId = project.Id, Name = "Todo", OrderIndex = 0, IsDoneColumn = false };
        db.Projects.Add(project);
        db.BoardColumns.Add(column);
        db.Tasks.Add(new TaskItem
        {
            Id = Guid.CreateVersion7(), ProjectId = project.Id, ColumnId = column.Id, Title = "Overdue",
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), CreatedBy = operatorUser.Id,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync();

        var service = new DashboardQueryService(db, new ChannelAccessGuard(db));
        var result = await service.ComputeAsync(MakePrincipal(operatorUser.Id), CancellationToken.None);

        Assert.Equal(0, result.ActionRequired.OverdueTasks.Count);
    }

    private static (Project Project, BoardColumn TodoColumn, BoardColumn DoneColumn) SeedProject(AppDbContext db)
    {
        var project = new Project { Id = Guid.CreateVersion7(), Name = "Test Project", Key = $"P{Guid.NewGuid():N}"[..8], CreatedAt = DateTimeOffset.UtcNow };
        var todo = new BoardColumn { Id = Guid.CreateVersion7(), ProjectId = project.Id, Name = "Todo", OrderIndex = 0, IsDoneColumn = false };
        var done = new BoardColumn { Id = Guid.CreateVersion7(), ProjectId = project.Id, Name = "Done", OrderIndex = 1, IsDoneColumn = true };
        db.Projects.Add(project);
        db.BoardColumns.AddRange(todo, done);
        return (project, todo, done);
    }

    private static TaskItem SeedTask(
        AppDbContext db, Project project, BoardColumn column, Guid assigneeId, DateOnly? dueDate, string title = "Task")
    {
        var task = new TaskItem
        {
            Id = Guid.CreateVersion7(), ProjectId = project.Id, ColumnId = column.Id, Title = title,
            AssigneeId = assigneeId, DueDate = dueDate, CreatedBy = assigneeId,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Tasks.Add(task);
        return task;
    }

    [Fact]
    public async Task ComputeAsync_MyWork_GroupsMyConversationsByStatusAndSumsUnreadCount()
    {
        var db = CreateDb();
        var me = SeedUser(db);
        var otherUser = SeedUser(db);
        var channel = SeedChannel(db);
        db.ChannelMembers.Add(new ChannelMember { ChannelId = channel.Id, UserId = me.Id });

        var mine1 = SeedConversation(db, channel, status: ConversationStatus.InProgress, assignedTo: me.Id);
        mine1.UnreadCount = 3;
        var mine2 = SeedConversation(db, channel, status: ConversationStatus.InProgress, assignedTo: me.Id);
        mine2.UnreadCount = 2;
        var mineClosed = SeedConversation(db, channel, status: ConversationStatus.Closed, assignedTo: me.Id);
        mineClosed.UnreadCount = 0;
        SeedConversation(db, channel, status: ConversationStatus.InProgress, assignedTo: otherUser.Id); // на ман — набояд ба ҳисоб равад

        await db.SaveChangesAsync();

        var service = new DashboardQueryService(db, new ChannelAccessGuard(db));
        var result = await service.ComputeAsync(MakePrincipal(me.Id), CancellationToken.None);

        Assert.Equal(5, result.MyWork.MyUnread); // 3 + 2 + 0
        var inProgress = result.MyWork.MyConversations.Single(g => g.Status == "InProgress");
        Assert.Equal(2, inProgress.Count);
        var closed = result.MyWork.MyConversations.Single(g => g.Status == "Closed");
        Assert.Equal(1, closed.Count);
    }

    [Fact]
    public async Task ComputeAsync_MyWork_ConversationAssignedToMeInAChannelIHaveNoAccessTo_IsExcluded()
    {
        var db = CreateDb();
        var me = SeedUser(db); // на Owner, на аъзои канал
        var channel = SeedChannel(db); // ман узви ин канал НЕСТАМ
        SeedConversation(db, channel, status: ConversationStatus.InProgress, assignedTo: me.Id).UnreadCount = 7;

        await db.SaveChangesAsync();

        var service = new DashboardQueryService(db, new ChannelAccessGuard(db));
        var result = await service.ComputeAsync(MakePrincipal(me.Id), CancellationToken.None);

        Assert.Empty(result.MyWork.MyConversations);
        Assert.Equal(0, result.MyWork.MyUnread);
    }

    [Fact]
    public async Task ComputeAsync_MyWork_IsAlwaysPersonalEvenForOwner()
    {
        var db = CreateDb();
        var owner = SeedUser(db);
        var otherUser = SeedUser(db);
        var channel = SeedChannel(db);
        SeedConversation(db, channel, status: ConversationStatus.InProgress, assignedTo: owner.Id);
        SeedConversation(db, channel, status: ConversationStatus.InProgress, assignedTo: otherUser.Id);
        SeedConversation(db, channel, status: ConversationStatus.New, assignedTo: null);

        await db.SaveChangesAsync();

        var service = new DashboardQueryService(db, new ChannelAccessGuard(db));
        var result = await service.ComputeAsync(MakePrincipal(owner.Id, RoleKeys.Owner), CancellationToken.None);

        // teamStats/actionRequired-и Owner ҳама чизро мебинад (тасдиқшуда дар тестҳои дигар),
        // вале myWork — не: танҳо чате, ки ба ХУДИ Owner вогузошта шудааст.
        var total = result.MyWork.MyConversations.Sum(g => g.Count);
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task ComputeAsync_MyWork_TasksToday_UsesOfficeLocalDate_NotRawUtc()
    {
        var db = CreateDb();
        var me = SeedUser(db);
        var (project, todoColumn, _) = SeedProject(db);
        db.ProjectMembers.Add(new ProjectMember { ProjectId = project.Id, UserId = me.Id });

        var now = DateTimeOffset.UtcNow;
        var localToday = OfficeLocalDate.Today(now);
        SeedTask(db, project, todoColumn, me.Id, localToday, title: "Имрӯз (маҳаллӣ)");

        await db.SaveChangesAsync();

        var service = new DashboardQueryService(db, new ChannelAccessGuard(db));
        var result = await service.ComputeAsync(MakePrincipal(me.Id), CancellationToken.None);

        Assert.Equal(1, result.MyWork.MyTasksToday.Count);
        var item = result.MyWork.MyTasksToday.Items.Single();
        Assert.Equal("Имрӯз (маҳаллӣ)", item.Title);
        Assert.Equal(project.Name, item.ProjectName);
        // ProjectId — линки фронт ба /projects/{id}?task={taskId} бе ин кор намекунад.
        Assert.Equal(project.Id, item.ProjectId);
    }

    [Fact]
    public async Task ComputeAsync_MyWork_TasksOverdue_ExcludesDoneColumn_SameDefinitionAsBlock1()
    {
        var db = CreateDb();
        var me = SeedUser(db);
        var (project, todoColumn, doneColumn) = SeedProject(db);
        db.ProjectMembers.Add(new ProjectMember { ProjectId = project.Id, UserId = me.Id });

        var yesterday = OfficeLocalDate.Today(DateTimeOffset.UtcNow).AddDays(-1);
        SeedTask(db, project, todoColumn, me.Id, yesterday, title: "Мӯҳлатгузашта");
        SeedTask(db, project, doneColumn, me.Id, yesterday, title: "Мӯҳлатгузашта, вале тамом шуда");

        await db.SaveChangesAsync();

        var service = new DashboardQueryService(db, new ChannelAccessGuard(db));
        var result = await service.ComputeAsync(MakePrincipal(me.Id), CancellationToken.None);

        Assert.Equal(1, result.MyWork.MyTasksOverdue.Count);
        var item = result.MyWork.MyTasksOverdue.Items.Single();
        Assert.Equal("Мӯҳлатгузашта", item.Title);
        Assert.Equal(project.Id, item.ProjectId);
    }
}
