using System.Linq.Expressions;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Office.Api.Auth;
using Office.Api.Data.Entities;

namespace Office.Api.Data;

/// <param name="tenantContext">
/// Who is asking — see <see cref="ApplyTenantFilters"/>. Null (tests, design-time tools) means
/// System: no filtering.
/// </param>
public class AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext? tenantContext = null)
    : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserPermission> UserPermissions => Set<UserPermission>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerRefreshToken> CustomerRefreshTokens => Set<CustomerRefreshToken>();
    public DbSet<CustomerExternalLogin> CustomerExternalLogins => Set<CustomerExternalLogin>();
    public DbSet<CustomerPasswordReset> CustomerPasswordResets => Set<CustomerPasswordReset>();
    public DbSet<SubscriptionRequest> SubscriptionRequests => Set<SubscriptionRequest>();

    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();
    public DbSet<BoardColumn> BoardColumns => Set<BoardColumn>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<TaskComment> TaskComments => Set<TaskComment>();
    public DbSet<TaskAttachment> TaskAttachments => Set<TaskAttachment>();
    public DbSet<Label> Labels => Set<Label>();
    public DbSet<TaskLabel> TaskLabels => Set<TaskLabel>();
    public DbSet<TaskActivity> TaskActivities => Set<TaskActivity>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<ChannelMember> ChannelMembers => Set<ChannelMember>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<ConversationAssignmentEvent> ConversationAssignmentEvents => Set<ConversationAssignmentEvent>();
    public DbSet<MessageTemplate> MessageTemplates => Set<MessageTemplate>();
    public DbSet<WebhookLog> WebhookLogs => Set<WebhookLog>();
    public DbSet<AutomationRule> AutomationRules => Set<AutomationRule>();
    public DbSet<AutomationRun> AutomationRuns => Set<AutomationRun>();

    public DbSet<Flow> Flows => Set<Flow>();
    public DbSet<FlowNode> FlowNodes => Set<FlowNode>();
    public DbSet<FlowEdge> FlowEdges => Set<FlowEdge>();
    public DbSet<FlowSession> FlowSessions => Set<FlowSession>();
    public DbSet<FlowSessionStep> FlowSessionSteps => Set<FlowSessionStep>();
    public DbSet<ContactTag> ContactTags => Set<ContactTag>();
    public DbSet<ContactVariable> ContactVariables => Set<ContactVariable>();
    public DbSet<FlowTemplate> FlowTemplates => Set<FlowTemplate>();
    public DbSet<DataDeletionRequest> DataDeletionRequests => Set<DataDeletionRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        ApplyTenantFilters(modelBuilder);
        ApplySnakeCaseNaming(modelBuilder);
    }

    // Read by the query filters at query time (EF parameterises members of the context
    // instance), so one cached model serves every caller.
    private TenantIdentity Tenant => tenantContext?.Current ?? new TenantIdentity(TenantScope.System, null);
    private TenantScope CurrentTenantScope => Tenant.Scope;
    private Guid? CurrentTenantCustomerId => Tenant.CustomerId;

    /// <summary>
    /// Data isolation between staff and мизоҷон (phase 14), in one place instead of a Where in
    /// each of the ~330 queries over these tables — a forgotten Where would leak, a forgotten
    /// filter cannot happen. Every table that hangs off a channel is listed with its path to
    /// the owner (Channel.CustomerId: null = company). Bypass only with IgnoreQueryFilters(),
    /// and only where a reviewer can see why.
    /// </summary>
    private void ApplyTenantFilters(ModelBuilder modelBuilder)
    {
        ApplyTenantFilter<Channel>(modelBuilder, c => c.CustomerId);
        ApplyTenantFilter<ChannelMember>(modelBuilder, m => m.Channel.CustomerId);
        ApplyTenantFilter<Conversation>(modelBuilder, c => c.Channel.CustomerId);
        ApplyTenantFilter<Message>(modelBuilder, m => m.Conversation.Channel.CustomerId);
        ApplyTenantFilter<ConversationAssignmentEvent>(modelBuilder, e => e.Conversation.Channel.CustomerId);
        ApplyTenantFilter<ContactTag>(modelBuilder, t => t.Contact.Channel.CustomerId);
        ApplyTenantFilter<ContactVariable>(modelBuilder, v => v.Contact.Channel.CustomerId);
        ApplyTenantFilter<AutomationRule>(modelBuilder, r => r.Channel.CustomerId);
        ApplyTenantFilter<AutomationRun>(modelBuilder, r => r.Rule.Channel.CustomerId);
        ApplyTenantFilter<Flow>(modelBuilder, f => f.Channel.CustomerId);
        ApplyTenantFilter<FlowNode>(modelBuilder, n => n.Flow.Channel.CustomerId);
        ApplyTenantFilter<FlowEdge>(modelBuilder, e => e.Flow.Channel.CustomerId);
        ApplyTenantFilter<FlowSession>(modelBuilder, s => s.Flow.Channel.CustomerId);
        ApplyTenantFilter<FlowSessionStep>(modelBuilder, s => s.Session.Flow.Channel.CustomerId);
    }

    private void ApplyTenantFilter<TEntity>(ModelBuilder modelBuilder, Expression<Func<TEntity, Guid?>> owner)
        where TEntity : class
    {
        // The rule, written once over the owner id. Customer also requires a known id, so a
        // Customer scope without one can never match company channels (owner id null).
        Expression<Func<Guid?, bool>> rule = ownerId =>
            CurrentTenantScope == TenantScope.System
            || (CurrentTenantScope == TenantScope.Staff && ownerId == null)
            || (CurrentTenantScope == TenantScope.Customer
                && CurrentTenantCustomerId != null
                && ownerId == CurrentTenantCustomerId);

        var body = ReplacingExpressionVisitor.Replace(rule.Parameters[0], owner.Body, rule.Body);
        modelBuilder.Entity<TEntity>().HasQueryFilter(Expression.Lambda<Func<TEntity, bool>>(body, owner.Parameters));
    }

    private static void ApplySnakeCaseNaming(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            entity.SetTableName(ToSnakeCase(entity.GetTableName()!));

            foreach (var property in entity.GetProperties())
                property.SetColumnName(ToSnakeCase(property.Name));
        }
    }

    private static string ToSnakeCase(string value)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsUpper(c))
            {
                if (i > 0)
                    builder.Append('_');
                builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
