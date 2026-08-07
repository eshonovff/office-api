using Office.Api.Sms;

namespace Office.Api.Tests.Sms;

public class OsonSmsRequestBuilderTests
{
    [Fact]
    public void BuildHash_KnownInputs_MatchesGoldenValue()
    {
        var hash = OsonSmsRequestBuilder.BuildHash(
            txnId: "txn123",
            login: "crmnizom",
            sender: "CRM NIZOM",
            phoneLocal: "927777777",
            secretHash: "e97ea76dc1892427330d70d644e3875e");

        Assert.Equal("146a29eecb465021b1c36613cf5ad3ffe73055e88ef6a03cc0336dec315c12f8", hash);
    }

    [Fact]
    public void BuildHash_DifferentTxnId_ProducesDifferentHash()
    {
        var hash1 = OsonSmsRequestBuilder.BuildHash("txn1", "login", "sender", "927777777", "secret");
        var hash2 = OsonSmsRequestBuilder.BuildHash("txn2", "login", "sender", "927777777", "secret");

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void BuildUrl_ContainsAllRequiredParameters()
    {
        var url = OsonSmsRequestBuilder.BuildUrl(
            serverUrl: "https://api.osonsms.com/sendsms_v1.php",
            login: "crmnizom",
            sender: "CRM NIZOM",
            phoneLocal: "927777777",
            message: "Салом!",
            txnId: "txn123",
            secretHash: "e97ea76dc1892427330d70d644e3875e");

        Assert.StartsWith("https://api.osonsms.com/sendsms_v1.php?", url);
        Assert.Contains("phone_number=927777777", url);
        Assert.Contains("login=crmnizom", url);
        Assert.Contains("txn_id=txn123", url);
        Assert.Contains("str_hash=", url);
        Assert.Contains("from=CRM%20NIZOM", url);
    }
}
