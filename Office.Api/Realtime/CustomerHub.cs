using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Office.Api.Auth;
using Office.Api.Common;

namespace Office.Api.Realtime;

/// <summary>
/// Realtime for a мизоҷ (their own chats). The only group a connection ever joins is the one
/// named after the customer id in its own token — there is no join method a client could call
/// with someone else's id. Events carry only ids ("this chat changed"); the page then reads the
/// data from /api/public, where the tenant filter applies.
/// </summary>
[Authorize(AuthenticationSchemes = AuthSchemes.Customer, Policy = AuthSchemes.CustomerOnlyPolicy)]
public class CustomerHub : Hub
{
    public const string ChatUpdatedEvent = "ChatUpdated";

    public static string CustomerGroupName(Guid customerId) => $"customer:{customerId}";

    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, CustomerGroupName(Context.User!.GetUserId()));
        await base.OnConnectedAsync();
    }
}
