using FluentAssertions;
using FunAndChecks.Application.Profiles;
using FunAndChecks.Domain.Enums;
using Xunit;

namespace FunAndChecks.Tests.Application;

/// <summary>
/// CSS достижений пишет человек, а видят его все, включая анонимных зрителей таблицы результатов.
/// Компилятор обязан пропускать только перечисленное и привязывать правила к элементам владельца.
/// </summary>
public class EffectCssCompilerTests
{
    private static string Css(string source) => EffectCssCompiler.Compile("7", source).Css;
    private static IReadOnlyList<EffectCssError> Errors(string source) => EffectCssCompiler.Compile("7", source).Errors;

    [Fact]
    public void Surfaces_AreRewrittenToSelectorsOfTheOwner()
    {
        Css("name { color: gold; }").Should().Be(".fc-fx-name[data-fx=\"7\"]{color:gold;}");
        // Картинка аватара обрезает содержимое: само правило — на ней, слои — на обёртке.
        Css("avatar { box-shadow: 0 0 0 2px red; }").Should().StartWith(".fc-fx-avatar[data-fx=\"7\"]>.fc-profile-media{");
        Css("avatar::after { content: \"\"; inset: -10%; }").Should().StartWith(".fc-fx-avatar[data-fx=\"7\"]::after{content:\"\";");
        // В таблице фон красит каждая ячейка строки, в очереди — сам блок.
        Css("row:hover { opacity: .9; }").Should().StartWith("tr.fc-fx-row[data-fx=\"7\"]:hover>td,.fc-fx-row[data-fx=\"7\"]:not(tr):hover{");
        Css("hero { border-color: red; }").Should().StartWith(".fc-fx-profile[data-fx=\"7\"] .fc-profile-hero{");
        Css("site i:nth-child(2n+1) { width: 20px; }").Should().StartWith(".fc-fx-site[data-fx=\"7\"]>i:nth-child(2n+1){");
        Css("name, hero::before { color: red; }").Should().StartWith(".fc-fx-name[data-fx=\"7\"],.fc-fx-profile[data-fx=\"7\"] .fc-profile-hero::before{");
    }

    [Theory]
    [InlineData("body { color: red; }")]
    [InlineData(".fc-sidebar { display: none; }")]
    [InlineData("name > a { color: red; }")]
    [InlineData("name i { color: red; }")]
    [InlineData("row::after { content: \"\"; }")]
    [InlineData("site:hover { opacity: 1; }")]
    [InlineData("name { color: red; } * { color: blue; }")]
    public void ForeignSelectors_AreErrors(string source) => Errors(source).Should().NotBeEmpty();

    [Theory]
    [InlineData("name { background: url(https://evil.example/track.png); }")]
    [InlineData("name { background: URL(data:image/png;base64,AAAA); }")]
    [InlineData("name { background-image: image-set(\"x.png\" 1x); }")]
    [InlineData("name { color: red !important; }")]
    [InlineData("name { color: expression(alert(1)); }")]
    [InlineData("name { color: red; } </style><script>alert(1)</script>")]
    [InlineData("name { content: \"\\41\"; }")]
    [InlineData("name { font-family: \"x\"; }")]
    [InlineData("@import \"https://evil.example/x.css\";")]
    [InlineData("@font-face { font-family: x; }")]
    [InlineData("@media screen { name { color: red; } }")]
    [InlineData("name { color: red; ")]
    [InlineData("hero { hero { color: red; } }")]
    public void DangerousOrBrokenInput_IsRejected_AndNothingIsEmitted(string source)
    {
        var result = EffectCssCompiler.Compile("7", source);
        result.Errors.Should().NotBeEmpty();
        result.Css.Should().BeEmpty();
    }

    [Theory]
    [InlineData("row { height: 80px; }")]
    [InlineData("row { padding: 20px; }")]
    [InlineData("row { display: none; }")]
    [InlineData("row { background: red; }")]
    [InlineData("row { font-size: 30px; }")]
    [InlineData("name { font-size: 30px; }")]
    [InlineData("name { display: block; }")]
    [InlineData("name { position: absolute; }")]
    [InlineData("site { background: black; }")]
    [InlineData("hero::after { position: fixed; }")]
    [InlineData("hero::after { z-index: 9999; }")]
    [InlineData("avatar::after { pointer-events: auto; }")]
    public void GeometryOfRowsAndEscapesFromTheOwner_AreRejected(string source) => Errors(source).Should().ContainSingle();

    [Fact]
    public void Keyframes_ArePrefixed_AndCannotAnimateGeometry()
    {
        var css = Css("@keyframes glow { from { opacity: .5; } 50%, to { opacity: 1; } } name { animation: glow 2s ease-in-out infinite; }");
        css.Should().Be("@keyframes fx7-glow{from{opacity:.5;}50%,to{opacity:1;}}.fc-fx-name[data-fx=\"7\"]{animation:fx7-glow 2s ease-in-out infinite;}");
        // Строка таблицы — фиксированной высоты, анимацией её тоже не раздвинуть.
        Errors("@keyframes grow { to { height: 90px; } } row { animation: grow 1s; }").Should().NotBeEmpty();
        // Чужую анимацию (например, сезонной темы) подключить нельзя.
        Errors("name { animation: fc-halloween-float 2s infinite; }").Should().ContainSingle();
        Css("name { animation: none; }").Should().Contain("animation:none;");
    }

    [Fact]
    public void Pictures_AreOnlyTheAchievementsOwnSlots()
    {
        Css("avatar::after { background: image(frame) center/contain no-repeat; }")
            .Should().Contain("background:var(--fx-img-frame) center/contain no-repeat;");
        Errors("avatar::after { background: image(secret); }").Should().ContainSingle().Which.Message.Should().Contain("image(secret)");
        EffectCssGenerator.ImageVariables("7", [(AchievementMediaSlot.Frame, Guid.Empty, false), (AchievementMediaSlot.Decor, Guid.Empty, true)])
            .Should().Be("[data-fx=\"7\"]{--fx-img-frame:url(\"/api/achievements/media/00000000-0000-0000-0000-000000000000\");" +
                "--fx-img-decor:url(\"/api/achievements/media/00000000-0000-0000-0000-000000000000?poster=true\");}");
    }

    [Fact]
    public void ThemesAndMedia_AreSupported_ErrorsCarryTheAuthorsLine()
    {
        Css("@light { name { color: #333; } }").Should().Be("html[data-theme=\"light\"] .fc-fx-name[data-fx=\"7\"]{color:#333;}");
        Css("@dark { row { color: white; } }").Should().StartWith("html:not([data-theme=\"light\"]) tr.fc-fx-row[data-fx=\"7\"]>td,html:not([data-theme=\"light\"]) .fc-fx-row");
        Css("@media (prefers-reduced-motion: reduce) { name { animation: none; } }")
            .Should().Be("@media (prefers-reduced-motion: reduce){.fc-fx-name[data-fx=\"7\"]{animation:none;}}");
        Errors("/* комментарий\nна две строки */\nname { color: gold; }\n\nrow { height: 1px; }").Should().ContainSingle().Which.Line.Should().Be(5);
        Errors(new string('a', EffectCssCompiler.MaxLength + 1)).Should().ContainSingle();
        EffectCssCompiler.Compile("7", "   ").Should().BeEquivalentTo(new EffectCssResult("", []));
    }

    [Fact]
    public void Constructor_ProducesScopedCss_AndRefusesBadValues()
    {
        var css = EffectCssGenerator.Generate("7", new AchievementEffects(
            Avatar: new AvatarEffect(Frame: true, RingColor: "#ff0000", RingWidth: 3, Animation: "spin"),
            Name: new NameEffect(Color: "#111111", Color2: "#eeeeee", Animation: "shimmer"),
            Row: new RowEffect(Color: "#00ff00", Opacity: 20, BarColor: "#0000ff"),
            Profile: new ProfileEffect(Banner: true, AccentColor: "#123456"),
            Site: new SiteEffect(Particles: 3, Motion: "fall")));
        css.Should().Contain(".fc-fx-avatar[data-fx=\"7\"]>.fc-profile-media{box-shadow:0 0 0 3px #ff0000;}");
        css.Should().Contain(".fc-fx-avatar[data-fx=\"7\"]::after{").And.Contain("var(--fx-img-frame)").And.Contain("@keyframes fx7-g-spin");
        css.Should().Contain("background-clip:text").And.Contain("@keyframes fx7-g-shimmer");
        css.Should().Contain("tr.fc-fx-row[data-fx=\"7\"]>td,.fc-fx-row[data-fx=\"7\"]:not(tr){background-image:linear-gradient(180deg,");
        css.Should().Contain("tr.fc-fx-row[data-fx=\"7\"]>td:first-child,.fc-fx-row[data-fx=\"7\"]:not(tr){box-shadow:inset 3px 0 0 #0000ff;}");
        css.Should().Contain("var(--fx-img-banner)").And.Contain("--fc-accent:#123456");
        css.Should().Contain(".fc-fx-site[data-fx=\"7\"]>i:nth-child(-n+3){").And.Contain("i:nth-child(3){").And.NotContain("i:nth-child(4){");
        EffectCssGenerator.Generate("7", new AchievementEffects()).Should().BeEmpty();

        // Значения конструктора попадают в CSS как есть, поэтому всё, что не цвет и не из списка, — отказ.
        EffectCssGenerator.Build("7", new AchievementEffects(Name: new NameEffect(Color: "red;} body{display:none")), null).Ok.Should().BeFalse();
        EffectCssGenerator.Build("7", new AchievementEffects(Avatar: new AvatarEffect(Animation: "explode")), null).Ok.Should().BeFalse();
        EffectCssGenerator.Build("7", new AchievementEffects(Site: new SiteEffect(Particles: 500)), null).Ok.Should().BeFalse();
        // Свой CSS идёт после настроек конструктора и может их переопределить.
        EffectCssGenerator.Build("7", new AchievementEffects(Name: new NameEffect(Color: "#111111")), "name { color: gold; }").Css
            .Should().Be(".fc-fx-name[data-fx=\"7\"]{color:#111111;}.fc-fx-name[data-fx=\"7\"]{color:gold;}");
    }
}
