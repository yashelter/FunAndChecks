using Frontend.Shared.Models;

namespace Frontend.Shared.Api;

/// <summary>Эндпоинты студентов — /api/students.</summary>
public class StudentsApi(HttpClient http) : ApiClientBase(http)
{
    public Task<StudentDto> GetAsync(Guid studentId, CancellationToken ct = default) =>
        GetAsync<StudentDto>($"api/students/{studentId}", ct);

    public Task<StudentDetailsDto> GetDetailsAsync(Guid studentId, CancellationToken ct = default) =>
        GetAsync<StudentDetailsDto>($"api/students/{studentId}/details", ct);

    public Task<List<TaskWithStatusDto>> GetTasksWithStatusAsync(Guid studentId, int subjectId, CancellationToken ct = default) =>
        GetAsync<List<TaskWithStatusDto>>($"api/students/{studentId}/subjects/{subjectId}/tasks", ct);

    public Task<List<StudentGradeDto>> GetGradesAsync(Guid studentId, int subjectId, CancellationToken ct = default) =>
        GetAsync<List<StudentGradeDto>>($"api/students/{studentId}/subjects/{subjectId}/grades", ct);
}
