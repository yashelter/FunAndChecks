using Frontend.Shared.Models;
using Frontend.Shared.Resources;
using Microsoft.Extensions.Localization;

namespace Frontend.Shared.Api;

/// <summary>Эндпоинты студентов — /api/students.</summary>
public class StudentsApi(HttpClient http, IStringLocalizer<AppStrings> loc) : ApiClientBase(http, loc)
{
    public Task<StudentDto> GetAsync(Guid studentId, CancellationToken ct = default) =>
        GetAsync<StudentDto>($"api/students/{studentId}", ct);

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

    public Task<UserAccountPageDto> GetAccountsAsync(string? query, int page = 1, int pageSize = 25, CancellationToken ct = default) =>
        GetAsync<UserAccountPageDto>($"api/students/accounts?query={Uri.EscapeDataString(query ?? string.Empty)}&page={page}&pageSize={pageSize}", ct);

    public Task DeleteAccountAsync(Guid userId, CancellationToken ct = default) =>
        DeleteAsync($"api/students/accounts/{userId}", ct);
}
