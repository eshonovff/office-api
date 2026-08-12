using Office.Api.Channels.WhatsApp;

namespace Office.Api.Tests.Channels.WhatsApp;

public class InternalNoteGuardTests
{
    [Fact]
    public void CanDispatchToProvider_InternalNote_ReturnsFalse()
    {
        // Ин ягона санҷиши воқеан ба провайдер расидан/нарасидани ёддоштро гарантия
        // мекунад — WhatsAppSendJob маҳз ҳамин методро дар аввали SendAsync даъват мекунад.
        Assert.False(InternalNoteGuard.CanDispatchToProvider(isInternalNote: true));
    }

    [Fact]
    public void CanDispatchToProvider_RegularMessage_ReturnsTrue()
    {
        Assert.True(InternalNoteGuard.CanDispatchToProvider(isInternalNote: false));
    }
}
