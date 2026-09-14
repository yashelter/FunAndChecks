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

    [Fact]
    public async Task GetOrAddAsync_InvalidatedDuringLoad_DoesNotPublishStaleResult()
    {
        var sut = new ResultsCacheService();
        var firstLoadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstLoad = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callCount = 0;

        async Task<SubjectResultsDto> Factory()
        {
            var call = Interlocked.Increment(ref callCount);
            if (call == 1)
            {
                firstLoadStarted.SetResult();
                await releaseFirstLoad.Task;
                return new SubjectResultsDto(1, "stale", [], [], []);
            }

            return new SubjectResultsDto(1, "fresh", [], [], []);
        }

        var loading = sut.GetOrAddAsync(1, Factory);
        await firstLoadStarted.Task;
        sut.Invalidate(1);
        releaseFirstLoad.SetResult();

        var result = await loading;

        result.SubjectName.Should().Be("fresh");
        sut.GetResults(1)!.SubjectName.Should().Be("fresh");
        callCount.Should().Be(2);
    }
}
