using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Office.Api.Common;

namespace Office.Api.Realtime;

[Authorize]
public class InboxHub : Hub
{
    public static string UserGroupName(Guid userId) => $"user:{userId}";

    public static string ChannelGroupName(Guid channelId) => $"channel:{channelId}";

    public override async Task OnConnectedAsync()
    {
        var userId = Context.User!.GetUserId();
        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroupName(userId));
        await base.OnConnectedAsync();
    }

    // Канал ва channel_members ҳанӯз вуҷуд надоранд (фазаи 4). Ин методи омодагӣ
    // барои фазаи 6 аст — санҷиши узвияти канал баъдтар, ҳамон замон ки Channel
    // entity сохта мешавад, ба ин ҷо илова мешавад.
    public async Task JoinChannel(Guid channelId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, ChannelGroupName(channelId));
    }
}
