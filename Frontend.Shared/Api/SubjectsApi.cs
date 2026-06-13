using Frontend.Shared.Models;

namespace Frontend.Shared.Api;

/// <summary>Эндпоинты предметов, задач и оценочных колонок — /api/subjects.</summary>
public class SubjectsApi(HttpClient http) : ApiClientBase(http)
{
    public Task<List<SubjectDto>> GetAllAsync(CancellationToken ct = default) =>
        GetAsync<List<SubjectDto>>("api/subjects", ct);

    public Task<SubjectDto> GetAsync(int subjectId, CancellationToken ct = default) =>
        GetAsync<SubjectDto>($"api/subjects/{subjectId}", ct);

    public Task<SubjectDto> CreateAsync(CreateSubjectRequest request, CancellationToken ct = default) =>
        PostAsync<SubjectDto>("api/subjects", request, ct);

    public Task DeleteAsync(int subjectId, CancellationToken ct = default) =>
        DeleteAsync($"api/subjects/{subjectId}", ct);

    public Task<List<TaskDto>> GetTasksAsync(int subjectId, CancellationToken ct = default) =>
        GetAsync<List<TaskDto>>($"api/subjects/{subjectId}/tasks", ct);

    public Task<TaskDto> CreateTaskAsync(int subjectId, CreateTaskRequest request, CancellationToken ct = default) =>
        PostAsync<TaskDto>($"api/subjects/{subjectId}/tasks", request, ct);

    public Task DeleteTaskAsync(int taskId, CancellationToken ct = default) =>
        DeleteAsync($"api/tasks/{taskId}", ct);

    public Task<List<StudentDetailsDto>> GetStudentsAsync(int subjectId, CancellationToken ct = default) =>
        GetAsync<List<StudentDetailsDto>>($"api/subjects/{subjectId}/students", ct);

    public Task<List<GradeComponentDto>> GetGradeComponentsAsync(int subjectId, CancellationToken ct = default) =>
        GetAsync<List<GradeComponentDto>>($"api/subjects/{subjectId}/grade-components", ct);

    public Task<GradeComponentDto> CreateGradeComponentAsync(int subjectId, CreateGradeComponentRequest request, CancellationToken ct = default) =>
        PostAsync<GradeComponentDto>($"api/subjects/{subjectId}/grade-components", request, ct);
}
