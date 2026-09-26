using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Office.Api.Auth;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Features.Conversations;
using Office.Api.Features.CustomerContacts;
using Office.Api.Features.DataDeletion;

namespace Office.Api.Tests.Features.CustomerContacts;

/// <summary>
/// The contacts endpoints called as мизоҷ A, through a DbContext with A's real tenant filter —
/// the same filter a request gets. B's contacts and the company's must be invisible to every
/// endpoint: not listed, not found by search, tag or channel, not in the export, and not
/// changeable or deletable (404, left untouched).
/// </summary>
public class CustomerContactsTests : IDisposable
{
    private sealed class FixedTenant(TenantIdentity identity) : ITenantContext
    {
        public TenantIdentity Current => identity;
    }

    private sealed class TestEnvironment(string root) : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = root;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "Office.Api";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = root;
        public string EnvironmentName { get; set; } = "Test";
    }

    private readonly string _databaseName = Guid.NewGuid().ToString();
    private readonly string _contentRoot = Path.Combine(Path.GetTempPath(), "contacts-tests-" + Guid.NewGuid());
    private readonly IConfiguration _configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Subscriptions:Plans:0:Tier"] = "Pro",
            ["Subscriptions:Plans:0:MonthlyPrice"] = "200",
            ["Subscriptions:Plans:0:Limits:ActiveAutomations"] = "10",
        })
        .Build();

    private readonly Guid _customerA = Guid.NewGuid();
    private readonly Guid _customerB = Guid.NewGuid();
    private readonly Guid _channelA = Guid.NewGuid();
    private readonly Guid _channelB = Guid.NewGuid();
    private readonly Guid _companyChannel = Guid.NewGuid();
    private readonly Guid _a1 = Guid.NewGuid(); // A's contact with everything
    private readonly Guid _a2 = Guid.NewGuid(); // A's contact whose name is a spreadsheet formula
    private readonly Guid _b1 = Guid.NewGuid(); // B's contact — same first name, same tag as a1
    private readonly Guid _c1 = Guid.NewGuid(); // the company's contact
    private readonly string _a1MediaPath;

    public CustomerContactsTests()
    {
        _a1MediaPath = $"whatsapp-media/{_channelA}/photo.jpg";
        using var db = Open(tenant: null);
        db.Customers.AddRange(
            new Customer { Id = _customerA, Email = "a@example.com", FullName = "A", TrialEndsAt = DateTimeOffset.UtcNow.AddDays(5) },
            new Customer { Id = _customerB, Email = "b@example.com", FullName = "B", TrialEndsAt = DateTimeOffset.UtcNow.AddDays(5) });
        db.Channels.AddRange(
            Channel(_channelA, "a.shop", "ig-a", _customerA),
            Channel(_channelB, "b.shop", "ig-b", _customerB),
            Channel(_companyChannel, "company", "ig-c", null));
        db.Conversations.AddRange(
            Contact(_a1, _channelA, "fan-1", "Нилуфар Ахмедова", "nilufar_a", minutesAgo: 5),
            Contact(_a2, _channelA, "fan-2", "=HYPERLINK(\"http://evil.example\",\"click\")", "trick", minutesAgo: 30),
            Contact(_b1, _channelB, "fan-1", "Нилуфар Каримова", "nilufar_b", minutesAgo: 1),
            Contact(_c1, _companyChannel, "fan-9", "Корманди ширкат", "company_fan", minutesAgo: 2));
        db.ContactTags.AddRange(
            new ContactTag { ContactId = _a1, Tag = "vip", CreatedAt = DateTimeOffset.UtcNow },
            new ContactTag { ContactId = _b1, Tag = "vip", CreatedAt = DateTimeOffset.UtcNow },
            new ContactTag { ContactId = _b1, Tag = "b-only", CreatedAt = DateTimeOffset.UtcNow });
        db.ContactVariables.AddRange(
            new ContactVariable { ContactId = _a1, Key = "phone", Value = "+992900000001" },
            new ContactVariable { ContactId = _b1, Key = "phone", Value = "+992900000002" });
        db.Messages.AddRange(
            new Message { Id = Guid.NewGuid(), ConversationId = _a1, Direction = MessageDirection.Inbound, Type = MessageType.Image, MediaUrl = _a1MediaPath, CreatedAt = DateTimeOffset.UtcNow },
            new Message { Id = Guid.NewGuid(), ConversationId = _b1, Direction = MessageDirection.Inbound, Type = MessageType.Text, Body = "B", CreatedAt = DateTimeOffset.UtcNow });

        var flowA = new Flow { Id = Guid.NewGuid(), ChannelId = _channelA, Name = "Нарх", TriggerType = "instagram_dm", TriggerConfigJson = "{}" };
        var flowB = new Flow { Id = Guid.NewGuid(), ChannelId = _channelB, Name = "B flow", TriggerType = "instagram_dm", TriggerConfigJson = "{}" };
        db.Flows.AddRange(flowA, flowB);
        var sessionA = new FlowSession { Id = Guid.NewGuid(), FlowId = flowA.Id, ContactId = _a1, Status = FlowSessionStatus.Finished, CreatedAt = DateTimeOffset.UtcNow };
        db.FlowSessions.AddRange(sessionA,
            new FlowSession { Id = Guid.NewGuid(), FlowId = flowB.Id, ContactId = _b1, CreatedAt = DateTimeOffset.UtcNow });
        db.FlowSessionSteps.Add(new FlowSessionStep { Id = Guid.NewGuid(), SessionId = sessionA.Id, NodeId = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow });

        var ruleA = new AutomationRule { Id = Guid.NewGuid(), ChannelId = _channelA, Name = "r", TriggerType = "instagram_comment", TriggerConfigJson = "{}", ActionConfigJson = "{}" };
        var ruleB = new AutomationRule { Id = Guid.NewGuid(), ChannelId = _channelB, Name = "r", TriggerType = "instagram_comment", TriggerConfigJson = "{}", ActionConfigJson = "{}" };
        db.AutomationRules.AddRange(ruleA, ruleB);
        db.AutomationRuns.AddRange(
            new AutomationRun { Id = Guid.NewGuid(), RuleId = ruleA.Id, TriggerExternalId = "ca-1", ActorExternalId = "fan-1", FollowCheckResult = FollowCheckResult.NotFollowing, CreatedAt = DateTimeOffset.UtcNow },
            // B's contact has the same Instagram id ("fan-1") on B's channel — must never count for A.
            new AutomationRun { Id = Guid.NewGuid(), RuleId = ruleB.Id, TriggerExternalId = "cb-1", ActorExternalId = "fan-1", FollowCheckResult = FollowCheckResult.Following, CreatedAt = DateTimeOffset.UtcNow.AddMinutes(1) });
        db.InstagramComments.AddRange(
            Comment(_channelA, "ca-1", "fan-1", parent: null),
            Comment(_channelA, "ca-1-reply", "ig-a", parent: "ca-1"),
            Comment(_channelB, "cb-1", "fan-1", parent: null));
        var broadcastA = new Broadcast { Id = Guid.NewGuid(), ChannelId = _channelA, Name = "A", Text = "a", ScheduledAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow };
        var broadcastB = new Broadcast { Id = Guid.NewGuid(), ChannelId = _channelB, Name = "B", Text = "b", ScheduledAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow };
        db.Broadcasts.AddRange(broadcastA, broadcastB);
        db.BroadcastRecipients.AddRange(
            new BroadcastRecipient { BroadcastId = broadcastA.Id, ContactId = _a1, Status = BroadcastRecipientStatus.Sent },
            new BroadcastRecipient { BroadcastId = broadcastA.Id, ContactId = _a2, Status = BroadcastRecipientStatus.Sent },
            new BroadcastRecipient { BroadcastId = broadcastB.Id, ContactId = _b1, Status = BroadcastRecipientStatus.Sent });
        db.SaveChanges();

        Directory.CreateDirectory(Path.Combine(_contentRoot, "uploads", "whatsapp-media", _channelA.ToString()));
        File.WriteAllText(Path.Combine(_contentRoot, "uploads", _a1MediaPath), "jpeg");
    }

    public void Dispose()
    {
        if (Directory.Exists(_contentRoot))
            Directory.Delete(_contentRoot, recursive: true);
    }

    private static Channel Channel(Guid id, string name, string externalId, Guid? owner) =>
        new() { Id = id, Type = ChannelType.Instagram, Name = name, ExternalId = externalId, CustomerId = owner, IsActive = true };

    private static Conversation Contact(Guid id, Guid channelId, string externalId, string name, string username, int minutesAgo) => new()
    {
        Id = id, ChannelId = channelId, ExternalId = externalId, ContactName = name, ContactUsername = username,
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-1), LastMessageAt = DateTimeOffset.UtcNow.AddMinutes(-minutesAgo),
        WindowExpiresAt = DateTimeOffset.UtcNow.AddHours(3),
    };

    private static InstagramComment Comment(Guid channelId, string id, string author, string? parent) => new()
    {
        Id = Guid.NewGuid(), ChannelId = channelId, ExternalId = id, MediaExternalId = "m1", ParentExternalId = parent,
        AuthorExternalId = author, Text = "нарх?", CommentedAt = DateTimeOffset.UtcNow, ReceivedAt = DateTimeOffset.UtcNow,
    };

    private AppDbContext Open(TenantIdentity? tenant) => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_databaseName).Options,
        tenant is null ? null : new FixedTenant(tenant.Value));

    private AppDbContext AsA() => Open(new TenantIdentity(TenantScope.Customer, _customerA));
    private AppDbContext Everything() => Open(tenant: null);

    private ClaimsPrincipal PrincipalA => new(new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, _customerA.ToString())], "test"));

    private static int? Status(IResult result) => (result as IStatusCodeHttpResult)?.StatusCode;

    private async Task<PagedResult<CustomerContactListItem>> List(string? search = null, string? tag = null, Guid? channelId = null)
    {
        await using var db = AsA();
        var result = await CustomerContactsEndpoints.ListAsync(channelId, search, tag, null, null, null, db, CancellationToken.None);
        return Assert.IsType<Ok<PagedResult<CustomerContactListItem>>>(result).Value!;
    }

    // ── What A can see ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_ShowsOnlyTheCallersContacts_NewestActivityFirst()
    {
        var page = await List();

        Assert.Equal([_a1, _a2], page.Items.Select(c => c.Id));
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(["vip"], page.Items[0].Tags);
        Assert.Equal("+992900000001", Assert.Single(page.Items[0].Variables).Value);
        Assert.NotNull(page.Items[0].CanMessageUntil);
    }

    [Fact]
    public async Task SearchTagAndChannel_NeverReachAnotherMizojsContacts()
    {
        Assert.Equal([_a1], (await List(search: "Нилуфар")).Items.Select(c => c.Id)); // B has a "Нилуфар" too
        Assert.Equal([_a1], (await List(search: "@NILUFAR")).Items.Select(c => c.Id));
        Assert.Equal([_a1], (await List(tag: "vip")).Items.Select(c => c.Id)); // B's contact is "vip" too
        Assert.Empty((await List(tag: "b-only")).Items);
        Assert.Empty((await List(channelId: _channelB)).Items);
        Assert.Empty((await List(channelId: _companyChannel)).Items);
        Assert.Empty((await List(search: "%")).Items); // no pattern characters in the search
    }

    [Fact]
    public async Task Tags_CountOnlyTheCallersContacts()
    {
        await using var db = AsA();
        var result = await CustomerContactsEndpoints.TagsAsync(null, db, CancellationToken.None);

        Assert.Equal([new ContactTagCount("vip", 1)], Assert.IsType<Ok<List<ContactTagCount>>>(result).Value);
    }

    [Fact]
    public async Task Card_OfAnotherMizojsOrTheCompanysContact_IsNotFound()
    {
        await using var db = AsA();
        Assert.Equal(404, Status(await CustomerContactsEndpoints.GetAsync(_b1, db, CancellationToken.None)));
        Assert.Equal(404, Status(await CustomerContactsEndpoints.GetAsync(_c1, db, CancellationToken.None)));
    }

    [Fact]
    public async Task Card_ShowsTheCallersOwnDetails_NeverAnothersWithTheSameInstagramId()
    {
        await using var db = AsA();
        var card = Assert.IsType<Ok<CustomerContactDetail>>(await CustomerContactsEndpoints.GetAsync(_a1, db, CancellationToken.None)).Value!;

        Assert.Equal(("Нилуфар Ахмедова", 1, 1), (card.Name, card.MessageCount, card.CommentCount));
        // B's run for the same Instagram id says "Following" and is newer — A must see A's own.
        Assert.Equal("NotFollowing", card.FollowStatus);
        Assert.Equal("Нарх", Assert.Single(card.Automations).FlowName);
    }

    // ── What A can change ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EveryChange_ToAnotherMizojsContact_IsNotFound_AndLeavesItUntouched()
    {
        await using (var db = AsA())
        {
            var ct = CancellationToken.None;
            Assert.Equal(404, Status(await CustomerContactsEndpoints.AddTagAsync(_b1, new AddContactTagRequest("x"), PrincipalA, db, _configuration, ct)));
            Assert.Equal(404, Status(await CustomerContactsEndpoints.RemoveTagAsync(_b1, "vip", PrincipalA, db, _configuration, ct)));
            Assert.Equal(404, Status(await CustomerContactsEndpoints.SetVariableAsync(_b1, new SetContactVariableRequest("phone", "x"), PrincipalA, db, _configuration, ct)));
            Assert.Equal(404, Status(await CustomerContactsEndpoints.RemoveVariableAsync(_b1, "phone", PrincipalA, db, _configuration, ct)));
            Assert.Equal(404, Status(await CustomerContactsEndpoints.DeleteAsync(_b1, PrincipalA, db, _configuration, new TestEnvironment(_contentRoot), NullLogger<Program>.Instance, ct)));
            Assert.Equal(404, Status(await CustomerContactsEndpoints.DeleteAsync(_c1, PrincipalA, db, _configuration, new TestEnvironment(_contentRoot), NullLogger<Program>.Instance, ct)));
        }

        await using var all = Everything();
        Assert.Equal(["b-only", "vip"], (await all.ContactTags.Where(t => t.ContactId == _b1).Select(t => t.Tag).ToListAsync()).Order());
        Assert.Equal("+992900000002", (await all.ContactVariables.SingleAsync(v => v.ContactId == _b1)).Value);
        Assert.True(await all.Conversations.AnyAsync(c => c.Id == _b1));
        Assert.True(await all.Conversations.AnyAsync(c => c.Id == _c1));
    }

    [Fact]
    public async Task TagsAndDetails_OnTheCallersContact_Change()
    {
        var ct = CancellationToken.None;
        await using (var db = AsA())
        {
            Assert.Equal(204, Status(await CustomerContactsEndpoints.AddTagAsync(_a1, new AddContactTagRequest("  lead  "), PrincipalA, db, _configuration, ct)));
            Assert.Equal(204, Status(await CustomerContactsEndpoints.AddTagAsync(_a1, new AddContactTagRequest("lead"), PrincipalA, db, _configuration, ct)));
            Assert.Equal(204, Status(await CustomerContactsEndpoints.RemoveTagAsync(_a1, "vip", PrincipalA, db, _configuration, ct)));
            Assert.Equal(204, Status(await CustomerContactsEndpoints.SetVariableAsync(_a1, new SetContactVariableRequest("phone", "+992900000009"), PrincipalA, db, _configuration, ct)));
            Assert.Equal(204, Status(await CustomerContactsEndpoints.SetVariableAsync(_a1, new SetContactVariableRequest("шаҳр", "Душанбе"), PrincipalA, db, _configuration, ct)));
            Assert.Equal(204, Status(await CustomerContactsEndpoints.RemoveVariableAsync(_a1, "шаҳр", PrincipalA, db, _configuration, ct)));
        }

        await using var all = Everything();
        Assert.Equal(["lead"], await all.ContactTags.Where(t => t.ContactId == _a1).Select(t => t.Tag).ToListAsync());
        Assert.Equal("+992900000009", (await all.ContactVariables.SingleAsync(v => v.ContactId == _a1)).Value);
    }

    [Fact]
    public async Task Tags_StopAtTheLimitPerContact()
    {
        await using var db = AsA();
        for (var i = 1; i < ContactLimits.MaxTagsPerContact; i++) // a1 already has "vip"
            await CustomerContactsEndpoints.AddTagAsync(_a1, new AddContactTagRequest($"t{i}"), PrincipalA, db, _configuration, CancellationToken.None);

        var result = await CustomerContactsEndpoints.AddTagAsync(_a1, new AddContactTagRequest("one-too-many"), PrincipalA, db, _configuration, CancellationToken.None);

        Assert.Equal(400, Status(result));
        Assert.Equal(ContactLimits.MaxTagsPerContact, await db.ContactTags.CountAsync(t => t.ContactId == _a1));
    }

    [Fact]
    public async Task WithoutAPlan_ChangesAndExportAreRefused_ButDeletingIsNot()
    {
        await using (var all = Everything())
        {
            (await all.Customers.SingleAsync(c => c.Id == _customerA)).TrialEndsAt = DateTimeOffset.UtcNow.AddDays(-1);
            await all.SaveChangesAsync();
        }

        var ct = CancellationToken.None;
        await using var db = AsA();
        Assert.Equal(403, Status(await CustomerContactsEndpoints.AddTagAsync(_a1, new AddContactTagRequest("x"), PrincipalA, db, _configuration, ct)));
        Assert.Equal(403, Status(await CustomerContactsEndpoints.SetVariableAsync(_a1, new SetContactVariableRequest("k", "v"), PrincipalA, db, _configuration, ct)));
        Assert.Equal(403, Status(await CustomerContactsEndpoints.ExportAsync(null, null, null, null, PrincipalA, db, _configuration, NullLogger<Program>.Instance, ct)));
        Assert.Equal(204, Status(await CustomerContactsEndpoints.DeleteAsync(_a2, PrincipalA, db, _configuration, new TestEnvironment(_contentRoot), NullLogger<Program>.Instance, ct)));
    }

    // ── Export ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Export_HasOnlyTheCallersRows_AndDefusesFormulas()
    {
        await using var db = AsA();
        var result = await CustomerContactsEndpoints.ExportAsync(null, null, null, null, PrincipalA, db, _configuration, NullLogger<Program>.Instance, CancellationToken.None);

        var file = Assert.IsType<FileContentHttpResult>(result);
        var bytes = file.FileContents.ToArray();
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        var csv = Encoding.UTF8.GetString(bytes[3..]);

        Assert.Contains("\"Нилуфар Ахмедова\"", csv);
        Assert.Contains("\"'+992900000001\"", csv); // a phone starting with + stays text, not a formula
        Assert.Contains("\"'=HYPERLINK(\"\"http://evil.example\"\",\"\"click\"\")\"", csv);
        Assert.DoesNotContain("Каримова", csv);
        Assert.DoesNotContain("+992900000002", csv);
        Assert.DoesNotContain("company_fan", csv);
        Assert.Equal(3, csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length); // header + a1 + a2
    }

    // ── Delete ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_RemovesTheContactAndEverythingAboutThem_AndOnlyThem()
    {
        await using (var db = AsA())
        {
            var result = await CustomerContactsEndpoints.DeleteAsync(
                _a1, PrincipalA, db, _configuration, new TestEnvironment(_contentRoot), NullLogger<Program>.Instance, CancellationToken.None);
            Assert.Equal(204, Status(result));
        }

        await using var all = Everything();
        Assert.False(await all.Conversations.AnyAsync(c => c.Id == _a1));
        Assert.False(await all.Messages.AnyAsync(m => m.ConversationId == _a1));
        Assert.False(await all.ContactTags.AnyAsync(t => t.ContactId == _a1));
        Assert.False(await all.ContactVariables.AnyAsync(v => v.ContactId == _a1));
        Assert.False(await all.FlowSessions.AnyAsync(s => s.ContactId == _a1));
        Assert.Empty(await all.FlowSessionSteps.ToListAsync());
        Assert.False(await all.AutomationRuns.AnyAsync(r => r.TriggerExternalId == "ca-1"));
        Assert.False(await all.InstagramComments.AnyAsync(c => c.ChannelId == _channelA)); // their comment and the reply under it
        Assert.False(await all.BroadcastRecipients.AnyAsync(r => r.ContactId == _a1));
        Assert.False(File.Exists(Path.Combine(_contentRoot, "uploads", _a1MediaPath)));

        // Everyone else stays: A's other contact, B's contact with the same Instagram id, the company's.
        Assert.True(await all.Conversations.AnyAsync(c => c.Id == _a2));
        Assert.True(await all.Conversations.AnyAsync(c => c.Id == _b1));
        Assert.True(await all.AutomationRuns.AnyAsync(r => r.TriggerExternalId == "cb-1"));
        Assert.True(await all.InstagramComments.AnyAsync(c => c.ExternalId == "cb-1"));
        Assert.True(await all.ContactTags.AnyAsync(t => t.ContactId == _b1));
        Assert.True(await all.BroadcastRecipients.AnyAsync(r => r.ContactId == _a2));
        Assert.True(await all.BroadcastRecipients.AnyAsync(r => r.ContactId == _b1));
        Assert.Equal(2, await all.Broadcasts.CountAsync()); // the broadcast itself stays — only this person's record goes
    }

    [Fact]
    public async Task Eraser_ByItself_KeepsRowsOfOtherChannels_EvenWithTheSameInstagramId()
    {
        // Without any tenant filter (a job's view): the eraser's own conditions must keep B's
        // runs and comments by the same Instagram id ("fan-1") out of it.
        await using var all = Everything();
        await ContactDataEraser.EraseAsync(all, _a1, CancellationToken.None);

        Assert.True(await all.AutomationRuns.AnyAsync(r => r.TriggerExternalId == "cb-1"));
        Assert.True(await all.InstagramComments.AnyAsync(c => c.ExternalId == "cb-1"));
        Assert.True(await all.FlowSessions.AnyAsync(s => s.ContactId == _b1));
        Assert.True(await all.Messages.AnyAsync(m => m.ConversationId == _b1));
        Assert.Equal(2, await all.ContactTags.CountAsync(t => t.ContactId == _b1));
    }

    [Fact]
    public void DeleteFiles_NeverLeavesTheUploadsFolder()
    {
        var outside = Path.Combine(_contentRoot, "outside.txt");
        File.WriteAllText(outside, "keep");

        ContactDataEraser.DeleteFiles(Path.Combine(_contentRoot, "uploads"), ["../outside.txt", "/etc/hosts"]);

        Assert.True(File.Exists(outside));
    }

    // ── CSV and input limits ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("=1+1", "\"'=1+1\"")]
    [InlineData("+992900000001", "\"'+992900000001\"")]
    [InlineData("-5", "\"'-5\"")]
    [InlineData("@SUM(A1)", "\"'@SUM(A1)\"")]
    [InlineData("\tx", "\"'\tx\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("Нилуфар", "\"Нилуфар\"")]
    public void CsvField_IsQuoted_AndNeverAFormula(string value, string expected) =>
        Assert.Equal(expected, ContactCsvWriter.Field(value));

    [Fact]
    public void Validators_BoundTagsAndDetails()
    {
        var tag = new AddContactTagRequestValidator();
        Assert.True(tag.Validate(new AddContactTagRequest("vip")).IsValid);
        Assert.False(tag.Validate(new AddContactTagRequest("  ")).IsValid);
        Assert.False(tag.Validate(new AddContactTagRequest(new string('t', ContactLimits.MaxTagLength + 1))).IsValid);
        Assert.False(tag.Validate(new AddContactTagRequest("a\nb")).IsValid);

        var variable = new SetContactVariableRequestValidator();
        Assert.True(variable.Validate(new SetContactVariableRequest("телефон", "+992")).IsValid);
        Assert.False(variable.Validate(new SetContactVariableRequest("", "x")).IsValid);
        Assert.False(variable.Validate(new SetContactVariableRequest("k", "")).IsValid);
        Assert.False(variable.Validate(new SetContactVariableRequest("k", new string('v', ContactLimits.MaxVariableValueLength + 1))).IsValid);
    }
}
