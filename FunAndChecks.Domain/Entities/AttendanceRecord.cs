using FunAndChecks.Domain.Enums;

namespace FunAndChecks.Domain.Entities;

public class AttendanceRecord
{
    public int SessionId { get; set; }
    public AttendanceSession Session { get; set; } = null!;
    public Guid StudentId { get; set; }
    public Student Student { get; set; } = null!;
    // Снимок группы: перевод студента не переписывает старый журнал.
    public int GroupId { get; set; }
    public required string GroupName { get; set; }
    public AttendanceStatus Status { get; set; }
    public Guid? MarkedByAdminId { get; set; }
    public Admin? MarkedByAdmin { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}
