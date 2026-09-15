using Frontend;
using Frontend.Shared;
using Frontend.Shared.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using System.Globalization;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Общие сервисы фронта: MudBlazor, локализация, аутентификация, HTTP-клиент к API и типизированные клиенты.
// API живёт на том же origin, что и SPA.
builder.Services.AddFrontendShared(builder.HostEnvironment.BaseAddress);

var host = builder.Build();

// Определяем культуру из localStorage или языка браузера, затем фиксируем её
// в CultureInfo до запуска рантайма — в .NET 10 это корректно загружает UI-ресурсы в WASM.
var cultureService = host.Services.GetRequiredService<CultureService>();
await cultureService.InitializeAsync();
CultureInfo.DefaultThreadCurrentCulture = new CultureInfo(cultureService.CurrentCulture);
CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo(cultureService.CurrentCulture);

await host.RunAsync();
