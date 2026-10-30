using System.Text.Json;
using System.Text.Json.Serialization;
using Frontend.Shared.Models;

namespace Frontend.Shared.Api;

/// <summary>
/// Разбор таблицы результатов кодом, сгенерированным при сборке, а не рефлексией: WASM работает в интерпретаторе,
/// и на больших предметах (100+ студентов × 100+ задач, ~0,5 МБ JSON) рефлексия заметно подвешивала страницу.
/// Опции — как у <c>ReadFromJsonAsync</c> по умолчанию (Web: camelCase, без учёта регистра).
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(SubjectResultsDto))]
[JsonSerializable(typeof(List<StudentColorUpdateDto>))]
internal sealed partial class ResultsJsonContext : JsonSerializerContext;
