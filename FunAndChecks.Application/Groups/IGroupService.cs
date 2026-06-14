using FunAndChecks.Application.Students;

namespace FunAndChecks.Application.Groups;

public interface IGroupService
{
    Task<List<GroupDto>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Группы, видимые админу: без запрещённых супер-админом и без скрытых самим админом (архив).</summary>
    Task<List<GroupDto>> GetVisibleForAdminAsync(Guid adminId, CancellationToken cancellationToken = default);

    Task<GroupDto> GetAsync(int groupId, CancellationToken cancellationToken = default);
    Task<GroupDto> CreateAsync(CreateGroupRequest request, CancellationToken cancellationToken = default);
    Task<GroupDto> UpdateAsync(int groupId, UpdateGroupRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(int groupId, CancellationToken cancellationToken = default);

    Task LinkSubjectAsync(int groupId, int subjectId, CancellationToken cancellationToken = default);
    Task UnlinkSubjectAsync(int groupId, int subjectId, CancellationToken cancellationToken = default);

    /// <summary>Id предметов, доступных группе.</summary>
    Task<List<int>> GetSubjectIdsAsync(int groupId, CancellationToken cancellationToken = default);

    /// <summary>Id групп, которым доступен предмет.</summary>
    Task<List<int>> GetGroupIdsForSubjectAsync(int subjectId, CancellationToken cancellationToken = default);

    Task<List<StudentDto>> GetStudentsAsync(int groupId, CancellationToken cancellationToken = default);
    Task<List<StudentDetailsDto>> GetStudentsDetailedAsync(int groupId, CancellationToken cancellationToken = default);
}
