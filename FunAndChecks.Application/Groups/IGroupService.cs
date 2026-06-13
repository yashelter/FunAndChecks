using FunAndChecks.Application.Students;

namespace FunAndChecks.Application.Groups;

public interface IGroupService
{
    Task<List<GroupDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<GroupDto> GetAsync(int groupId, CancellationToken cancellationToken = default);
    Task<GroupDto> CreateAsync(CreateGroupRequest request, CancellationToken cancellationToken = default);
    Task<GroupDto> UpdateAsync(int groupId, UpdateGroupRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(int groupId, CancellationToken cancellationToken = default);

    Task LinkSubjectAsync(int groupId, int subjectId, CancellationToken cancellationToken = default);
    Task UnlinkSubjectAsync(int groupId, int subjectId, CancellationToken cancellationToken = default);

    Task<List<StudentDto>> GetStudentsAsync(int groupId, CancellationToken cancellationToken = default);
    Task<List<StudentDetailsDto>> GetStudentsDetailedAsync(int groupId, CancellationToken cancellationToken = default);
}
