namespace AnnabaDigital.Domain.Security;

/// <summary>Permission catalog — single source of truth (seeded into the DB, never edited at runtime).</summary>
public static class Permissions
{
    public const string UsersRead = "users.read";
    public const string UsersCreate = "users.create";
    public const string UsersUpdate = "users.update";
    public const string UsersSuspend = "users.suspend";
    public const string UsersAssignRole = "users.assign_role";
    public const string InvitationsRead = "invitations.read";
    public const string InvitationsManage = "invitations.manage";
    public const string RolesRead = "roles.read";
    public const string RolesCreate = "roles.create";
    public const string RolesUpdate = "roles.update";
    public const string PermissionsRead = "permissions.read";
    public const string PermissionsAssign = "permissions.assign";
    public const string SessionsReadAll = "sessions.read_all";
    public const string SessionsRevoke = "sessions.revoke";
    public const string AuditRead = "audit.read";
    public const string SecurityRead = "security.read";
    public const string SettingsManage = "settings.manage";
    public const string TenantsManage = "tenants.manage";
    public const string UsersResetCredentials = "users.reset_credentials";
    // Helpdesk setup (keys follow the catalog rule resource.action, lower-case letters and underscores only)
    public const string LocationsRead = "locations.read";
    public const string LocationsManage = "locations.manage";
    public const string TeamsRead = "teams.read";
    public const string TeamsManage = "teams.manage";
    public const string CalendarsRead = "calendars.read";
    public const string CalendarsManage = "calendars.manage";
    public const string CatalogRead = "catalog.read";
    public const string CatalogManage = "catalog.manage";
    public const string FormsManage = "forms.manage";
    public const string TicketsReadTeam = "tickets.read_team";
    public const string TicketsReadAll = "tickets.read_all";
    public const string TicketsAssign = "tickets.assign";
    public const string TicketsTransition = "tickets.transition";
    public const string TicketsUpdate = "tickets.update";
    public const string TicketsCommentInternal = "tickets.comment_internal";
    public const string SlaRead = "sla.read";
    public const string SlaManage = "sla.manage";
    public const string RulesRead = "rules.read";
    public const string RulesManage = "rules.manage";
    public const string ApprovalsRead = "approvals.read";
    public const string ApprovalsManage = "approvals.manage";
    public const string RetentionManage = "retention.manage";
    public const string AssetsRead = "assets.read";
    public const string AssetsManage = "assets.manage";
    public const string AssetsImport = "assets.import";
    public const string WorkOrdersRead = "workorders.read";
    public const string WorkOrdersManage = "workorders.manage";
    public const string WorkOrdersComplete = "workorders.complete";
    public const string WorkOrdersCosts = "workorders.costs";
    public const string VendorsRead = "vendors.read";
    public const string VendorsManage = "vendors.manage";
    public const string PmRead = "pm.read";
    public const string PmManage = "pm.manage";
    public const string KbRead = "kb.read";
    public const string KbWrite = "kb.write";
    public const string KbPublish = "kb.publish";
    public const string CostRead = "cost.read";
    public const string BudgetsRead = "budgets.read";
    public const string BudgetsManage = "budgets.manage";
    public const string IntegrationsManage = "integrations.manage";
    public const string BookingsReadAll = "bookings.read_all";
    public const string BookingsManage = "bookings.manage";
    public const string ResourcesManage = "resources.manage";
    public const string VisitorsCheckIn = "visitors.checkin";
    public const string VisitorsReadAll = "visitors.read_all";
    public const string VisitorsManage = "visitors.manage";
    public const string MailManage = "mail.manage";
    public const string InventoryRead = "inventory.read";
    public const string InventoryManage = "inventory.manage";
    public const string InventoryAdjust = "inventory.adjust";
    public const string SuppliesFulfil = "supplies.fulfil";
    public const string DirectoryManage = "directory.manage";

    public sealed record Def(string Key, string Module, string Description, bool Critical = false);

    public static readonly IReadOnlyList<Def> Catalog =
    [
        new(UsersRead, "Users", "View users and their details"),
        new(UsersCreate, "Users", "Create users"),
        new(UsersUpdate, "Users", "Edit user profiles"),
        new(UsersSuspend, "Users", "Suspend, deactivate and unlock accounts"),
        new(UsersAssignRole, "Users", "Assign and remove roles on users", true),
        new(UsersResetCredentials, "Users", "Reset someone's two-step verification and require a new password", true),
        new(InvitationsRead, "Invitations", "View invitations"),
        new(InvitationsManage, "Invitations", "Create, resend and revoke invitations"),
        new(RolesRead, "Roles", "View roles"),
        new(RolesCreate, "Roles", "Create and duplicate roles"),
        new(RolesUpdate, "Roles", "Edit and archive roles"),
        new(PermissionsRead, "Permissions", "View permission catalog and access matrix"),
        new(PermissionsAssign, "Permissions", "Change role permission assignments", true),
        new(SessionsReadAll, "Sessions", "View sessions of other users"),
        new(SessionsRevoke, "Sessions", "Revoke sessions of other users"),
        new(AuditRead, "Monitoring", "View audit logs"),
        new(SecurityRead, "Monitoring", "View and acknowledge security events"),
        new(SettingsManage, "Administration", "Manage system settings", true),
        new(DirectoryManage, "Administration", "Connect the company directory: map SSO and SCIM groups to roles, and sync employee data (HR CSV) that can suspend people", true),
        new(TenantsManage, "Administration", "Create and suspend tenants (platform organisation only)", true),
        new(LocationsRead, "Helpdesk setup", "View sites, buildings, floors and rooms"),
        new(LocationsManage, "Helpdesk setup", "Create, edit and archive locations"),
        new(TeamsRead, "Helpdesk setup", "View teams and their members"),
        new(TeamsManage, "Helpdesk setup", "Create and edit teams and their members"),
        new(CalendarsRead, "Helpdesk setup", "View business calendars and holidays"),
        new(CalendarsManage, "Helpdesk setup", "Create and edit business calendars and holidays"),
        new(CatalogRead, "Service catalog", "View the service catalog (including unpublished and staff-only services) and form versions"),
        new(CatalogManage, "Service catalog", "Create, edit, publish and archive categories and services"),
        new(FormsManage, "Service catalog", "Edit the request form of a service (creates a new form version)"),
        new(TicketsReadTeam, "Tickets", "See the queue: tickets of your teams, assigned to you, or unassigned in your teams"),
        new(TicketsReadAll, "Tickets", "See every ticket in the organisation"),
        new(TicketsAssign, "Tickets", "Assign tickets to people and teams; confirm a resolution for the requester"),
        new(TicketsTransition, "Tickets", "Work tickets: start, hold, resume, resolve, cancel and reopen"),
        new(TicketsUpdate, "Tickets", "Change a ticket's priority, subject or location, and file requests on someone's behalf"),
        new(TicketsCommentInternal, "Tickets", "Write and read internal notes that requesters never see"),
        new(SlaRead, "Helpdesk setup", "View SLA policies and see how a ticket stands against its targets"),
        new(SlaManage, "Helpdesk setup", "Create, edit, reorder and archive SLA policies"),
        new(RulesRead, "Helpdesk setup", "View assignment rules and test where a ticket would go"),
        new(RulesManage, "Helpdesk setup", "Create, edit, reorder and archive assignment rules"),
        new(ApprovalsRead, "Helpdesk setup", "View which services need approval and who approves"),
        new(ApprovalsManage, "Helpdesk setup", "Set, change and remove the approval policy of a service"),
        new(RetentionManage, "Administration", "Set how long tickets, files and notifications are kept, place legal holds and run the clean-up", true),
        new(AssetsRead, "Assets", "View the asset register, an asset's history and the requests about it"),
        new(AssetsManage, "Assets", "Add, edit, move, hand out, retire and restore assets, and manage asset types"),
        new(AssetsImport, "Assets", "Import assets from a CSV file (creates and updates many at once)"),
        new(WorkOrdersRead, "Work orders", "See the work orders of tickets you can see, and the ones given to you"),
        new(WorkOrdersManage, "Work orders", "Create, change, assign and cancel work orders, decide whether a ticket resolves with them, and manage checklist templates"),
        new(WorkOrdersComplete, "Work orders", "Do work orders given to you: start, hold, tick the checklist, log time, add photos and complete"),
        new(WorkOrdersCosts, "Work orders", "See and record the parts and costs of work orders"),
        new(VendorsRead, "Vendors", "See vendors, their contacts, places, contracts and documents"),
        new(VendorsManage, "Vendors", "Add and edit vendors, block and unblock them, and manage their contracts and documents"),
        new(PmRead, "Maintenance", "See preventive-maintenance plans, their visits and the compliance report"),
        new(PmManage, "Maintenance", "Create, change, pause and remove preventive-maintenance plans"),
        new(KbRead, "Knowledge base", "Read staff-only knowledge-base articles (everyone reads the articles meant for everyone)"),
        new(KbWrite, "Knowledge base", "Write knowledge-base drafts, add files and link articles to services"),
        new(KbPublish, "Knowledge base", "Publish and archive knowledge-base articles and manage their categories"),
        new(CostRead, "Costs", "See the cost report (all work-order costs of the organisation) and export it"),
        new(BudgetsRead, "Costs", "See budgets and how much of them is spent, and the cost centres"),
        new(BudgetsManage, "Costs", "Set budgets, cost centres, the currency and the labour rate"),
        new(IntegrationsManage, "Integrations", "Create and revoke API keys and manage webhooks (they give other systems access to the organisation's data)", Critical: true),
        new(BookingsReadAll, "Bookings", "See who booked what and why on every resource (others only see that a slot is taken)"),
        new(BookingsManage, "Bookings", "Book for other people, and change or cancel anyone's booking"),
        new(VisitorsCheckIn, "Visitors", "Work the reception desk: register walk-in visitors for anyone, check visitors in and out, hand out badges"),
        new(VisitorsReadAll, "Visitors", "See every visit and the visitor report (visitors' names, companies and contact details)"),
        new(VisitorsManage, "Visitors", "Keep the visitor block list and set the visitor rules: the acknowledgement, ID digits and how long visitors' details are kept"),
        new(InventoryRead, "Inventory", "See stock items, how much is where, the movements, the reorder list and stock-takes"),
        new(InventoryManage, "Inventory", "Add and edit stock items, receive deliveries, issue stock and transfer it between places"),
        new(InventoryAdjust, "Inventory", "Correct stock with an adjustment and run stock-takes (counts post their differences)"),
        new(SuppliesFulfil, "Inventory", "Work the supplies desk: see every supply request, approve those without a manager, issue the items from stock and close back-orders"),
        new(MailManage, "Visitors", "Work the mailroom: log incoming letters and parcels, record who collected them, and log outgoing mail with its courier and waybill"),
        new(ResourcesManage, "Bookings", "Add, edit and archive bookable resources: their place, capacity, features, opening hours and who may book them"),
    ];

    public static IEnumerable<string> All => Catalog.Select(c => c.Key);
}

/// <summary>Seed roles (matches docs/09-rbac-matrix.md).</summary>
public static class SystemRoles
{
    public const string SuperAdmin = "super_admin";
    public const string UserAdmin = "user_admin";
    public const string SecurityAuditor = "security_auditor";
    public const string User = "user";
    public const string GaAgent = "ga_agent";
    public const string GaSupervisor = "ga_supervisor";
    public const string GaManager = "ga_manager";
    public const string Vendor = "vendor";
    public const string Receptionist = "receptionist";

    private static readonly string[] AgentPermissions =
        [Permissions.TicketsReadTeam, Permissions.TicketsTransition, Permissions.TicketsUpdate, Permissions.TicketsCommentInternal, Permissions.CatalogRead, Permissions.AssetsRead, Permissions.WorkOrdersRead, Permissions.WorkOrdersComplete, Permissions.VendorsRead, Permissions.PmRead, Permissions.KbRead, Permissions.KbWrite, Permissions.InventoryRead];
    private static readonly string[] SupervisorPermissions = [.. AgentPermissions, Permissions.BookingsReadAll, Permissions.BookingsManage, Permissions.VisitorsReadAll, Permissions.InventoryAdjust, Permissions.SuppliesFulfil, Permissions.TicketsReadAll, Permissions.TicketsAssign, Permissions.TeamsRead, Permissions.SlaRead, Permissions.RulesRead, Permissions.ApprovalsRead, Permissions.WorkOrdersManage, Permissions.WorkOrdersCosts, Permissions.PmManage, Permissions.KbPublish, Permissions.CostRead, Permissions.BudgetsRead];

    public sealed record Def(string Key, string Name, string Description, bool Protected, string[] Permissions);

    public static readonly IReadOnlyList<Def> Seed =
    [
        new(SuperAdmin, "Super Admin", "Unrestricted access. Permission set is immutable.", true, [.. Permissions.All]),
        new(UserAdmin, "User Admin", "Manages users and invitations.", false,
        [
            Permissions.UsersRead, Permissions.UsersCreate, Permissions.UsersUpdate, Permissions.UsersSuspend, Permissions.UsersAssignRole,
            Permissions.InvitationsRead, Permissions.InvitationsManage, Permissions.RolesRead, Permissions.SessionsReadAll, Permissions.SessionsRevoke, Permissions.DirectoryManage,
        ]),
        new(SecurityAuditor, "Security Auditor", "Read-only access to monitoring and sessions.", false,
        [
            Permissions.UsersRead, Permissions.RolesRead, Permissions.PermissionsRead, Permissions.SessionsReadAll, Permissions.AuditRead, Permissions.SecurityRead,
        ]),
        new(User, "User", "Self-service only. No administrative permissions.", false, []),
        new(GaAgent, "GA Agent", "Works tickets of their teams: start, hold, resolve, internal notes.", false, AgentPermissions),
        new(GaSupervisor, "GA Supervisor", "Agent rights plus every ticket, assignment and confirming resolutions.", false, SupervisorPermissions),
        new(GaManager, "GA Manager", "Supervisor rights plus running the helpdesk's configuration: locations, calendars, teams, service catalog and forms, SLA policies, assignment rules and approval policies. No user administration.", false, [.. SupervisorPermissions, Permissions.LocationsRead, Permissions.LocationsManage, Permissions.CalendarsRead, Permissions.CalendarsManage, Permissions.TeamsManage, Permissions.CatalogManage, Permissions.FormsManage, Permissions.SlaManage, Permissions.RulesManage, Permissions.ApprovalsManage, Permissions.AssetsManage, Permissions.AssetsImport, Permissions.VendorsManage, Permissions.BudgetsManage, Permissions.ResourcesManage, Permissions.VisitorsCheckIn, Permissions.VisitorsManage, Permissions.MailManage, Permissions.InventoryManage]),
        new(Receptionist, "Receptionist", "Works the reception desk and the mailroom: registers walk-in visitors, checks visitors in and out, sees the visitor list, and logs incoming and outgoing mail.", false, [Permissions.VisitorsCheckIn, Permissions.VisitorsReadAll, Permissions.MailManage]),
        new(Vendor, "Vendor", "A vendor's person: sees and works only the work orders given to their company (link the person to the vendor on the vendor's page).", false, [Permissions.WorkOrdersRead, Permissions.WorkOrdersComplete]),
    ];
}
