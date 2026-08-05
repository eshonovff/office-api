using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Office.Api.Common;

namespace Office.Api.Realtime;

[Authorize]
public class BoardHub(IProjectAccessGuard access) : Hub
{
    public static string GroupName(Guid projectId) => $"project:{projectId}";

    public override async Task OnConnectedAsync()
    {
        var projectIdValue = Context.GetHttpContext()?.Request.Query["projectId"].ToString();

        if (!Guid.TryParse(projectIdValue, out var projectId))
        {
            Context.Abort();
            return;
        }

        // Санҷиши узвият ҳатман ПЕШ аз ҳамроҳ кардан ба гурӯҳ — то корбари бегона ба
        // board-и проекте, ки узви он нест, гӯш карда натавонад.
        var hasAccess = await access.HasAccessAsync(Context.User!, projectId, Context.ConnectionAborted);
        if (!hasAccess)
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(projectId));
        await base.OnConnectedAsync();
    }
}
