using FunAndChecks.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FunAndChecks.Infrastructure.Persistence.Configurations;

/// <summary>
/// Базовый класс пользователей маппится по стратегии TPC:
/// отдельные таблицы Students и Admins, без общей таблицы Users.
/// </summary>
public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.UseTpcMappingStrategy();

        builder.HasKey(u => u.Id);
        // Id задаётся приложением (совпадает с Id учётной записи).
        builder.Property(u => u.Id).ValueGeneratedNever();

        builder.Property(u => u.FirstName).HasMaxLength(100);
        builder.Property(u => u.LastName).HasMaxLength(100);

        builder.Ignore(u => u.FullName);
    }
}
