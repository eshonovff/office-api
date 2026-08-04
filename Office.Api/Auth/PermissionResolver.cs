namespace Office.Api.Auth;

public static class PermissionResolver
{
    /// <summary>
    /// Формула: (∪ роль-permission-ҳо) + (иҷозати шахсӣ) − (манъи шахсӣ).
    /// Манъ ҳамеша болотар аз иҷозат.
    /// </summary>
    public static IReadOnlySet<string> Resolve(
        IEnumerable<string> rolePermissionKeys,
        IEnumerable<(string PermissionKey, bool IsGranted)> userExceptions,
        bool isOwner)
    {
        if (isOwner)
            return Permissions.All;

        var exceptions = userExceptions.ToList();
        var granted = new HashSet<string>(rolePermissionKeys);

        foreach (var exception in exceptions.Where(e => e.IsGranted))
            granted.Add(exception.PermissionKey);

        foreach (var exception in exceptions.Where(e => !e.IsGranted))
            granted.Remove(exception.PermissionKey);

        return granted;
    }
}
