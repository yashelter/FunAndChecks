using FluentAssertions;
using Frontend.Shared.Models;
using Frontend.Shared.Services;
using Frontend.Shared.UI;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using NSubstitute;
using Xunit;

namespace FunAndChecks.Tests;

/// <summary>Поиск, фильтр и сортировка таблицы результатов; состояние возврата из профиля.</summary>
public class FrontendResultsTableTests
{
    private static StudentResultRowDto Row(string name, string group, int total, int rank) =>
        new(Guid.NewGuid(), name, group, total, [], [], null, Rank: rank);

    private static readonly List<StudentResultRowDto> Rows =
    [
        Row("Яковлев Пётр Ильич", "М-1", 10, 3),
        Row("Абрамова Анна Олеговна", "М-2", 30, 1),
        Row("Борисов Борис Борисович", "М-1", 20, 2),
        Row("абрамов Глеб", "М-2", 10, 3),
    ];

    [Fact]
    public void Apply_WithoutFilterAndSort_ReturnsTheSameList()
    {
        // Та же ссылка: таблице не нужно пересчитывать видимые строки, порядок — как отдал сервер.
        ResultsRowLocator.Apply(Rows, ResultsViewState.Default).Should().BeSameAs(Rows);
    }

    [Fact]
    public void Apply_Search_IgnoresCaseAndSurroundingSpaces()
    {
        var found = ResultsRowLocator.Apply(Rows, new ResultsViewState(Search: "  АБРАМ "));

        found.Select(r => r.FullName).Should().Equal("Абрамова Анна Олеговна", "абрамов Глеб");
    }

    [Fact]
    public void Apply_GroupAndSearch_AreCombined()
    {
        var found = ResultsRowLocator.Apply(Rows, new ResultsViewState(Search: "бор", Group: "М-1"));

        found.Should().ContainSingle().Which.FullName.Should().Be("Борисов Борис Борисович");
        ResultsRowLocator.Apply(Rows, new ResultsViewState(Search: "бор", Group: "М-2")).Should().BeEmpty();
    }

    [Fact]
    public void Apply_SortByTotalDescending_KeepsServerOrderForEqualTotals()
    {
        var sorted = ResultsRowLocator.Apply(Rows, new ResultsViewState(SortKey: ResultsViewState.SortTotal, Descending: true));

        sorted.Select(r => r.FullName).Should().Equal(
            "Абрамова Анна Олеговна", "Борисов Борис Борисович", "Яковлев Пётр Ильич", "абрамов Глеб");
    }

    [Fact]
    public void Apply_SortByName_IgnoresCase()
    {
        var sorted = ResultsRowLocator.Apply(Rows, new ResultsViewState(SortKey: ResultsViewState.SortName));

        sorted.Select(r => r.FullName).Should().Equal(
            "абрамов Глеб", "Абрамова Анна Олеговна", "Борисов Борис Борисович", "Яковлев Пётр Ильич");
    }

    [Fact]
    public void Apply_SortByExtraColumn_UsesItsKey()
    {
        var variants = new Dictionary<Guid, int> { [Rows[0].StudentId] = 2, [Rows[2].StudentId] = 1 };
        Func<StudentResultRowDto, IComparable> byVariant = r => variants.GetValueOrDefault(r.StudentId, int.MaxValue);

        var sorted = ResultsRowLocator.Apply(Rows, new ResultsViewState(SortKey: "variant:7"),
            key => key == "variant:7" ? byVariant : null);

        sorted.Take(2).Select(r => r.FullName).Should().Equal("Борисов Борис Борисович", "Яковлев Пётр Ильич");
        // Колонки с таким ключом уже нет (набор вариантов удалили) — порядок сервера, а не исключение.
        ResultsRowLocator.Apply(Rows, new ResultsViewState(SortKey: "variant:8"), _ => null).Should().BeSameAs(Rows);
    }

    [Fact]
    public void VisibleIndex_CountsRowsAfterFilterAndSort()
    {
        var visible = ResultsRowLocator.Apply(Rows, new ResultsViewState(Group: "М-1", SortKey: ResultsViewState.SortRank));

        ResultsRowLocator.VisibleIndex(visible, Rows[0].StudentId).Should().Be(1);
        ResultsRowLocator.VisibleIndex(visible, Rows[2].StudentId).Should().Be(0);
        ResultsRowLocator.VisibleIndex(visible, Rows[1].StudentId).Should().Be(-1, "the group filter hides this student");
    }

    [Fact]
    public void ToggleSort_CyclesAscendingDescendingNone()
    {
        var state = ResultsViewState.Default.ToggleSort(ResultsViewState.SortName);
        state.Should().Be(new ResultsViewState(SortKey: ResultsViewState.SortName));

        state = state.ToggleSort(ResultsViewState.SortName);
        state.Descending.Should().BeTrue();

        state.ToggleSort(ResultsViewState.SortName).Should().Be(ResultsViewState.Default);
        // Другая колонка — снова с возрастания.
        state.ToggleSort(ResultsViewState.SortTotal).Should().Be(new ResultsViewState(SortKey: ResultsViewState.SortTotal));
    }

    [Fact]
    public void WithoutFilter_KeepsSorting()
    {
        var state = new ResultsViewState("иван", "М-1", ResultsViewState.SortTotal, true);

        state.HasFilter.Should().BeTrue();
        state.WithoutFilter().Should().Be(new ResultsViewState(SortKey: ResultsViewState.SortTotal, Descending: true));
    }

    [Theory]
    [InlineData("Иванов Иван Иванович", "Иванов И. И.")]
    [InlineData("Петрова  анна", "Петрова А.")]
    [InlineData("Мадонна", "Мадонна")]
    [InlineData("", "")]
    public void ShortName_KeepsSurnameAndInitials(string full, string expected)
    {
        ResultsColumns.ShortName(full).Should().Be(expected);
    }

    [Fact]
    public async Task NavigationState_RestoresViewOnceAndOnlyForAnotherTableInstance()
    {
        var state = new ResultsNavigationState(Substitute.For<AuthenticationStateProvider>(), Substitute.For<IJSRuntime>());
        var view = new ResultsViewState("иван", "М-1", ResultsViewState.SortTotal, true);
        Guid source = Guid.NewGuid(), returned = Guid.NewGuid();
        state.SelectSubject("/student", 5);

        await state.CaptureAsync("/student", view, source);

        state.ReturnUrl.Should().Be("/student");
        state.TakeRestore("/student", source).Should().BeNull("the table that captured the state already shows it");
        state.TakeRestore("/student", returned).Should().Be(view);
        state.TakeRestore("/student", returned).Should().BeNull("the state is restored once");
    }

    [Fact]
    public async Task NavigationState_ForgetsViewWhenSubjectChanges()
    {
        var state = new ResultsNavigationState(Substitute.For<AuthenticationStateProvider>(), Substitute.For<IJSRuntime>());
        state.SelectSubject("/admin", 5);
        await state.CaptureAsync("/admin", new ResultsViewState("иван"), Guid.NewGuid());

        state.SelectSubject("/admin", 6);

        state.TakeRestore("/admin", Guid.NewGuid()).Should().BeNull();
    }
}
