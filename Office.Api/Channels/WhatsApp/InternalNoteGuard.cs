namespace Office.Api.Channels.WhatsApp;

/// <summary>
/// Ягона санҷиши "ин ба провайдер расида метавонад?" — pure, бе DB. WhatsAppSendJob маҳз
/// ҳамин методро дар аввали SendAsync даъват мекунад (на нусхаи дигар ё санҷиши шабеҳ), то
/// он ба воқеан "ҷои фиристодан" мутобиқ бошад, на танҳо ба сатҳи endpoint — ҳатто агар бо
/// хатогӣ ёддошти дохилӣ enqueue шавад, ин ҷо мебояд ба провайдер нарасад.
/// </summary>
public static class InternalNoteGuard
{
    public static bool CanDispatchToProvider(bool isInternalNote) => !isInternalNote;
}
