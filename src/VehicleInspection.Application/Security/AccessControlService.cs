namespace VehicleInspection.Application.Security;

public sealed class AccessControlService
{
    private static readonly IReadOnlyDictionary<Role, Permission[]> Permissions = new Dictionary<Role, Permission[]>
    {
        [Role.Viewer] = new[]
        {
            Permission.ViewDashboard,
            Permission.ViewReports
        },
        [Role.Operator] = new[]
        {
            Permission.ViewDashboard,
            Permission.ViewReports,
            Permission.ExportReports,
            Permission.EditOperatorNotes
        },
        [Role.Admin] = Enum.GetValues<Permission>()
    };

    public bool Can(Role role, Permission permission)
    {
        return Permissions.TryGetValue(role, out var permissions) && permissions.Contains(permission);
    }
}
