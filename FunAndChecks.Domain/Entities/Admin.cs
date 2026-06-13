namespace FunAndChecks.Domain.Entities;

/// <summary>
/// Преподаватель/администратор — проверяет сдачи.
/// </summary>
public class Admin : User
{
    /// <summary>Цвет, которым подсвечиваются проверенные им работы в таблице результатов.</summary>
    public string? Color { get; set; }

    /// <summary>Буква-метка админа в таблице результатов (например, первая буква имени).</summary>
    public string? Letter { get; set; }
}
