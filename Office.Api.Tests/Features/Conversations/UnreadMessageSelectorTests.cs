using Office.Api.Data.Entities;
using Office.Api.Features.Conversations;

namespace Office.Api.Tests.Features.Conversations;

public class UnreadMessageSelectorTests
{
    [Theory]
    [InlineData(MessageDeliveryStatus.Pending)]
    [InlineData(MessageDeliveryStatus.Sent)]
    [InlineData(MessageDeliveryStatus.Delivered)]
    [InlineData(MessageDeliveryStatus.Failed)]
    public void ShouldMarkAsRead_InboundAndNotAlreadyRead_ReturnsTrue(MessageDeliveryStatus status)
    {
        Assert.True(UnreadMessageSelector.ShouldMarkAsRead(MessageDirection.Inbound, status));
    }

    [Fact]
    public void ShouldMarkAsRead_InboundAlreadyRead_ReturnsFalse()
    {
        Assert.False(UnreadMessageSelector.ShouldMarkAsRead(MessageDirection.Inbound, MessageDeliveryStatus.Read));
    }

    [Theory]
    [InlineData(MessageDeliveryStatus.Pending)]
    [InlineData(MessageDeliveryStatus.Sent)]
    [InlineData(MessageDeliveryStatus.Delivered)]
    [InlineData(MessageDeliveryStatus.Read)]
    [InlineData(MessageDeliveryStatus.Failed)]
    public void ShouldMarkAsRead_Outbound_AlwaysReturnsFalse(MessageDeliveryStatus status)
    {
        Assert.False(UnreadMessageSelector.ShouldMarkAsRead(MessageDirection.Outbound, status));
    }
}
