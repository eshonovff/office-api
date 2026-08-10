using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Office.Api.Common;

namespace Office.Api.Realtime;

[Authorize]
public class InboxHub(IChannelAccessGuard access) : Hub
{
    public static string UserGroupName(Guid userId) => $"user:{userId}";

    public static string ChannelGroupName(Guid channelId) => $"channel:{channelId}";

    public override async Task OnConnectedAsync()
    {
        var userId = Context.User!.GetUserId();
        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroupName(userId));
        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Ҳамон санҷиши IChannelAccessGuard-и REST endpoint-ҳо (ChannelMembers/Owner-Admin) —
    /// вагарна ягон корбари воридшуда метавонист ба ягон channelId ҳамроҳ шавад ва ҷараёни
    /// пурраи паёмҳои он каналро гирад. `assignedTo: null` қасдан аст: ин гурӯҳ тамоми
    /// сӯҳбатҳои каналро мебарорад (на якеро), пас корбари `only_assigned` набояд ҳамроҳ
    /// шавад — ӯ паёмҳои ба худаш таъиншударо тавассути гурӯҳи `user:{id}` (ҳамеша дар
    /// OnConnectedAsync ҳамроҳшуда) мегирад.
    /// HubException (на Context.Abort()) — рад як JoinChannel набояд пайвасти пурраро
    /// қатъ кунад, зеро як connection метавонад якчанд канали дигарро дуруст ҳамроҳ шуда бошад.
    /// </summary>
    public async Task JoinChannel(Guid channelId)
    {
        if (!await access.HasAccessAsync(Context.User!, channelId, assignedTo: null, Context.ConnectionAborted))
            throw new HubException("Дастрасӣ ба ин канал нест.");

        await Groups.AddToGroupAsync(Context.ConnectionId, ChannelGroupName(channelId));
    }
}
