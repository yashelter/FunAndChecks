using Frontend;
using Frontend.Shared;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Общие сервисы фронта: MudBlazor, аутентификация, HTTP-клиент к API и типизированные клиенты.
// API живёт на том же origin, что и SPA.
builder.Services.AddFrontendShared(builder.HostEnvironment.BaseAddress);

await builder.Build().RunAsync();
