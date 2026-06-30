using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using FunAndChecks.Application.Results;
using FunAndChecks.Infrastructure.Caching;
using Xunit;

namespace FunAndChecks.Tests;

public class ResultsCacheServiceTests
{
    [Fact]
    public async Task GetOrAddAsync_ConcurrentCalls_InvokesFactoryOnce()
    {
        // Arrange
        var sut = new ResultsCacheService();
        int subjectId = 1;
        int callCount = 0;
        
        Task<SubjectResultsDto> Factory()
        {
            Interlocked.Increment(ref callCount);
            return Task.FromResult(new SubjectResultsDto(subjectId, "Test Subject", [], [], []));
        }

        // Act
        var tasks = new Task<SubjectResultsDto>[50];
        for (int i = 0; i < 50; i++)
        {
            tasks[i] = sut.GetOrAddAsync(subjectId, Factory);
        }

        var results = await Task.WhenAll(tasks);

        // Assert
        callCount.Should().Be(1);
        results.Should().AllSatisfy(x => x.SubjectId.Should().Be(subjectId));
    }
}
