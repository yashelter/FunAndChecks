namespace FunAndChecks.Domain.Enums;

/// <summary>Вид чата: тема раздела (предмет или «Общее»), личный чат двоих или «Избранное» одного админа.</summary>
public enum ForumTopicKind
{
    Section = 0,
    Direct = 1,
    Saved = 2,
}
