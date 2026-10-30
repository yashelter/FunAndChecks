namespace FunAndChecks.Domain.Enums;

/// <summary>Сколько студентов может взять один вариант набора.</summary>
public enum VariantUniqueness
{
    /// <summary>Повторы разрешены.</summary>
    AllowRepeats = 0,
    /// <summary>Один вариант — один студент во всём предмете.</summary>
    UniqueInSubject = 1,
    /// <summary>Один вариант — один студент внутри группы.</summary>
    UniqueInGroup = 2,
}
