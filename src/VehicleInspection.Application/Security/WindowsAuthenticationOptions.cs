namespace VehicleInspection.Application.Security;

public sealed class WindowsAuthenticationOptions
{
    public string? ActiveDirectoryDomain { get; init; }
    public string? ActiveDirectoryServer { get; init; }
    public string AdministratorsGroupName { get; init; } = "Administrators";
    public string OperatorsGroupName { get; init; } = "Operators";
    public string ViewersGroupName { get; init; } = "Viewers";

    public bool HasActiveDirectoryConfiguration => !string.IsNullOrWhiteSpace(ActiveDirectoryDomain) || !string.IsNullOrWhiteSpace(ActiveDirectoryServer);
}
