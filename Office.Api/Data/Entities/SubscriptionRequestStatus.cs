namespace Office.Api.Data.Entities;

public enum SubscriptionRequestStatus
{
    /// <summary>Amount issued to the customer, no receipt uploaded yet.</summary>
    AwaitingPayment,

    /// <summary>Receipt uploaded, waiting for a moderator.</summary>
    Pending,

    Approved,
    Rejected,

    /// <summary>An AwaitingPayment request superseded by a newer request from the same customer.</summary>
    Cancelled,
}
