using FunAndChecks.Application.Students;

namespace FunAndChecks.Application.Groups;

public interface IGroupService
{
    Task<List<GroupDto>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Группы, видимые админу: без запрещённых супер-админом и без скрытых самим админом (архив).</summary>
    Task<List<GroupDto>> GetVisibleForAdminAsync(Guid adminId, CancellationToken cancellationToken = default);

    Task<GroupDto> GetAsync(int groupId, CancellationToken cancellationToken = default);
    Task<GroupDto> CreateAsync(CreateGroupRequest request, CancellationToken cancellationToken = default);
    Task<GroupDto> UpdateAsync(Guid adminId, int groupId, UpdateGroupRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid adminId, int groupId, CancellationToken cancellationToken = default);

    Task LinkSubjectAsync(Guid adminId, int groupId, int subjectId, CancellationToken cancellationToken = default);
    Task UnlinkSubjectAsync(Guid adminId, int groupId, int subjectId, CancellationToken cancellationToken = default);

    /// <summary>Id предметов, доступных группе.</summary>
    Task<List<int>> GetSubjectIdsAsync(Guid adminId, int groupId, CancellationToken cancellationToken = default);

    /// <summary>Id групп, которым доступен предмет.</summary>
    Task<List<int>> GetGroupIdsForSubjectAsync(Guid adminId, int subjectId, CancellationToken cancellationToken = default);

    Task<List<StudentDto>> GetStudentsAsync(int groupId, CancellationToken cancellationToken = default);
    Task<List<StudentDetailsDto>> GetStudentsDetailedAsync(Guid adminId, int groupId, CancellationToken cancellationToken = default);
}
