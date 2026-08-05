namespace Office.Api.Auth;

public static class Permissions
{
    public static class Users
    {
        public const string View = "users.view";
        public const string Manage = "users.manage";
    }

    public static class Roles
    {
        public const string View = "roles.view";
        public const string Manage = "roles.manage";
    }

    public static class Projects
    {
        public const string View = "projects.view";
        public const string Manage = "projects.manage";
    }

    public static class Tasks
    {
        public const string View = "tasks.view";
        public const string Create = "tasks.create";
        public const string Edit = "tasks.edit";
        public const string Delete = "tasks.delete";
        public const string Assign = "tasks.assign";
        public const string Move = "tasks.move";
    }

    public static class Inbox
    {
        public const string View = "inbox.view";
        public const string Reply = "inbox.reply";
        public const string Assign = "inbox.assign";
        public const string Close = "inbox.close";
        public const string Delete = "inbox.delete";
    }

    public static class Channels
    {
        public const string Manage = "channels.manage";
    }

    public static class Templates
    {
        public const string Manage = "templates.manage";
    }

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        Users.View, Users.Manage,
        Roles.View, Roles.Manage,
        Projects.View, Projects.Manage,
        Tasks.View, Tasks.Create, Tasks.Edit, Tasks.Delete, Tasks.Assign, Tasks.Move,
        Inbox.View, Inbox.Reply, Inbox.Assign, Inbox.Close, Inbox.Delete,
        Channels.Manage,
        Templates.Manage,
    };
}
