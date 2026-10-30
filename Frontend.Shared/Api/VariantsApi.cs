using Frontend.Shared.Models;
using Frontend.Shared.Resources;
using Microsoft.Extensions.Localization;

namespace Frontend.Shared.Api;

public class VariantsApi(HttpClient http, IStringLocalizer<AppStrings> loc) : ApiClientBase(http, loc)
{
    public Task<List<VariantSetDto>> GetSetsAsync(int subjectId) =>
        GetAsync<List<VariantSetDto>>($"api/variants/subjects/{subjectId}/sets");
    public Task<VariantSetDto> CreateSetAsync(int subjectId, SaveVariantSetRequest request) =>
        PostAsync<VariantSetDto>($"api/variants/subjects/{subjectId}/sets", request);
    public Task<VariantSetDto> UpdateSetAsync(int setId, SaveVariantSetRequest request) =>
        PutAsync<VariantSetDto>($"api/variants/sets/{setId}", request);
    public Task DeleteSetAsync(int setId) => DeleteAsync($"api/variants/sets/{setId}");

    public Task<VariantDto> AddVariantAsync(int setId, AddVariantRequest request) =>
        PostAsync<VariantDto>($"api/variants/sets/{setId}/variants", request);
    public Task<int> AddRangeAsync(int setId, AddVariantRangeRequest request) =>
        PostAsync<int>($"api/variants/sets/{setId}/variants/range", request);
    public Task<VariantDto> UpdateVariantAsync(int variantId, UpdateVariantRequest request) =>
        PutAsync<VariantDto>($"api/variants/variants/{variantId}", request);
    public Task DeleteVariantAsync(int variantId) => DeleteAsync($"api/variants/variants/{variantId}");

    public Task<VariantBoardDto> GetBoardAsync(int subjectId) =>
        GetAsync<VariantBoardDto>($"api/variants/subjects/{subjectId}/board");
    public Task<List<StudentVariantSetDto>> GetForStudentAsync(int subjectId, Guid studentId) =>
        GetAsync<List<StudentVariantSetDto>>($"api/variants/subjects/{subjectId}/students/{studentId}");
    public Task AssignAsync(int setId, Guid studentId, int variantId) =>
        PutAsync($"api/variants/sets/{setId}/students/{studentId}", new AssignVariantRequest(variantId));
    public Task UnassignAsync(int setId, Guid studentId) =>
        DeleteAsync($"api/variants/sets/{setId}/students/{studentId}");

    public Task<List<StudentVariantSetDto>> GetMyAsync(int subjectId) =>
        GetAsync<List<StudentVariantSetDto>>($"api/me/variants/subjects/{subjectId}");
    public Task<StudentVariantSetDto> ClaimAsync(int setId, int variantId) =>
        PostAsync<StudentVariantSetDto>($"api/me/variants/sets/{setId}/claim", new AssignVariantRequest(variantId));
}
