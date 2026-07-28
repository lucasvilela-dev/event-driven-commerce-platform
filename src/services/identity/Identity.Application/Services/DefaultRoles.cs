namespace Identity.Application.Services;

public static class DefaultRoles
{
    public const string Customer = "customer";
    public const string Admin = "admin";

    public static readonly IReadOnlyCollection<string> All = [Customer, Admin];
}