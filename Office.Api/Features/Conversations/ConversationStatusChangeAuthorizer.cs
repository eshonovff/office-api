using Office.Api.Data.Entities;

namespace Office.Api.Features.Conversations;

/// <summary>Тибқи 03-permissions/phase-6: пӯшидани чат (`Closed`) permission-и иловагии `inbox.close` металабад.</summary>
public static class ConversationStatusChangeAuthorizer
{
    public static bool RequiresClosePermission(ConversationStatus newStatus) => newStatus == ConversationStatus.Closed;
}
