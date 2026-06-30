using Frontend.Shared.Models;

namespace Frontend.Shared.Api;

/// <summary>Эндпоинты студентов — /api/students.</summary>
public class StudentsApi(HttpClient http) : ApiClientBase(http)
{
    public Task<StudentDto> GetAsync(Guid studentId, CancellationToken ct = default) =>
        GetAsync<StudentDto>($"api/students/{studentId}", ct);

    public Task<List<StudentDetailsDto>> SearchAsync(string query, CancellationToken ct = default) =>
        GetAsync<List<StudentDetailsDto>>($"api/students/search?query={Uri.EscapeDataString(query)}", ct);

    public Task SetColorAsync(Guid studentId, SetStudentColorRequest request, CancellationToken ct = default) =>
        PutAsync($"api/students/{studentId}/color", request, ct);

    public Task<StudentDetailsDto> GetDetailsAsync(Guid studentId, CancellationToken ct = default) =>
        GetAsync<StudentDetailsDto>($"api/students/{studentId}/details", ct);

    public Task<List<TaskWithStatusDto>> GetTasksWithStatusAsync(Guid studentId, int subjectId, CancellationToken ct = default) =>
        GetAsync<List<TaskWithStatusDto>>($"api/students/{studentId}/subjects/{subjectId}/tasks", ct);

    public Task<List<StudentGradeDto>> GetGradesAsync(Guid studentId, int subjectId, CancellationToken ct = default) =>
        GetAsync<List<StudentGradeDto>>($"api/students/{studentId}/subjects/{subjectId}/grades", ct);

    public Task UpdateAccountAsync(Guid studentId, UpdateStudentAccountRequest request, CancellationToken ct = default) =>
        PutAsync($"api/students/{studentId}/account", request, ct);
}
