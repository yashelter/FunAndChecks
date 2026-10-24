namespace FunAndChecks.Export;

/// <summary>Фамилия и имя для отдельных столбцов XLSX (склеенное ФИО неудобно сортировать и сопоставлять).</summary>
internal static class PersonName
{
    /// <summary>Берёт поля как есть; если их нет, делит «Фамилия Имя» по первому пробелу.</summary>
    public static (string Last, string First) Split(string? lastName, string? firstName, string fullName)
    {
        if (!string.IsNullOrWhiteSpace(lastName) || !string.IsNullOrWhiteSpace(firstName))
            return (lastName?.Trim() ?? "", firstName?.Trim() ?? "");
        var name = fullName.Trim();
        var space = name.IndexOf(' ');
        return space < 0 ? (name, "") : (name[..space], name[(space + 1)..].Trim());
    }
}
