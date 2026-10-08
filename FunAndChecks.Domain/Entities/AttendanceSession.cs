namespace FunAndChecks.Domain.Entities;

/// <summary>Занятие, для которого преподаватель решил вести учёт посещаемости.</summary>
public class AttendanceSession
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public int SubjectId { get; set; }
    public Subject Subject { get; set; } = null!;
    public Guid? CreatedByAdminId { get; set; }
    public Admin? CreatedByAdmin { get; set; }
    public ICollection<Group> Groups { get; set; } = new List<Group>();
    public ICollection<AttendanceRecord> Records { get; set; } = new List<AttendanceRecord>();
}
