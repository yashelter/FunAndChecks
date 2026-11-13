using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Frontend.Shared.Profiles;
using Xunit;
using ServerEffects = FunAndChecks.Application.Profiles.AchievementEffects;
using EffectCssGenerator = FunAndChecks.Application.Profiles.EffectCssGenerator;

namespace FunAndChecks.Tests;

/// <summary>
/// Инструкция docs/achievement-effects.md — то, по чему модель пишет оформление. Её примеры обязаны проходить
/// ту же проверку, что и сохранение на сервере, иначе инструкция разойдётся с кодом незаметно.
/// </summary>
public class FrontendAchievementGuideTests
{
    [Fact]
    public async Task Guide_IsEmbedded_AndNamesEverySlotAndSurface()
    {
        var text = await AchievementGuideText.LoadAsync();
        text.Should().StartWith("# Оформление достижений");
        foreach (var word in new[] { "`badge`", "`frame`", "`pattern`", "`banner`", "`decor`", "`avatar`", "`name`", "`row`", "`profile`", "`hero`", "`site`" })
            text.Should().Contain(word);
    }

    [Fact]
    public async Task EveryJsonExample_IsReadByTheEditor_AndAcceptedByTheServer()
    {
        var text = await AchievementGuideText.LoadAsync();
        var examples = Regex.Matches(text, "```json\\n(.*?)```", RegexOptions.Singleline).Select(m => m.Groups[1].Value).ToList();
        examples.Should().HaveCountGreaterThanOrEqualTo(4);
        foreach (var example in examples)
        {
            var draft = AchievementDraft.Parse(example);
            draft.Name.Should().NotBeNullOrWhiteSpace();
            // Клиент сериализует эффекты так же, как их читает сервер (camelCase) — проверяем тем же путём.
            var effects = ServerEffects.Parse(JsonSerializer.Serialize(draft.Effects, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            var result = EffectCssGenerator.Build("1", effects, draft.CustomCss);
            result.Errors.Should().BeEmpty($"the example \"{draft.Name}\" must pass the server check");
        }
    }

    [Fact]
    public void Draft_ReadsAModelsAnswer_WithFenceCommentsAndTrailingCommas()
    {
        var answer = """
            Вот оформление:

            ```json
            {
              // золото
              "name": "Золото",
              "effects": { "name": { "color": "#f5b301", "bold": true, }, },
            }
            ```
            Картинки не нужны.
            """;
        var draft = AchievementDraft.Parse(answer);
        draft.Name.Should().Be("Золото");
        draft.Effects!.Name.Color.Should().Be("#f5b301");
        draft.Effects.Name.Bold.Should().BeTrue();
        // Разделы, которых нет в JSON, — значения по умолчанию; поля верхнего уровня — «не менять».
        draft.Effects.Avatar.RingWidth.Should().Be(2);
        draft.Description.Should().BeNull();
        draft.CustomCss.Should().BeNull();

        AchievementDraft.Parse("{\"description\":\"x\"}").Effects.Should().BeNull();
        var invalid = () => AchievementDraft.Parse("это не JSON");
        invalid.Should().Throw<JsonException>();
    }

    [Fact]
    public void Draft_RoundTrips_AndKeepsCyrillicReadable()
    {
        var draft = new AchievementDraft("Зимняя сессия", "Описание", true,
            new AchievementEffects { Site = new SiteEffect { Particles = 5 } }, "name { color: #7cc4ff; }");
        var json = draft.ToJson();
        json.Should().Contain("Зимняя сессия").And.Contain("\"particles\": 5").And.NotContain("\"ringColor\"");
        var back = AchievementDraft.Parse(json);
        back.Name.Should().Be(draft.Name);
        back.ActiveByDefault.Should().BeTrue();
        back.Effects!.Site.Particles.Should().Be(5);
        back.CustomCss.Should().Be(draft.CustomCss);
    }
}
