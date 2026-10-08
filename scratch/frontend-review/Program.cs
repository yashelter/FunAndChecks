using Frontend.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Net;

var services = new ServiceCollection();
services.AddSingleton<IJSRuntime, FakeJs>();
services.AddSingleton<NavigationManager, FakeNav>();
services.AddScoped<CultureService>();
services.AddTransient<ProbeHandler>();
services.AddHttpClient("Api").AddHttpMessageHandler<ProbeHandler>().ConfigurePrimaryHttpMessageHandler(() => new StubHandler());
using var provider = services.BuildServiceProvider();
var rootCulture = provider.GetRequiredService<CultureService>();
await rootCulture.InitializeAsync();
Console.WriteLine("Root culture=" + rootCulture.CurrentCulture);
using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("Api");
await client.GetAsync("https://example.invalid");

class FakeJs : IJSRuntime {
 public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => ValueTask.FromResult((T)(object)"ru-RU");
 public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<T>(identifier, args);
}
class FakeNav : NavigationManager { public FakeNav() { Initialize("https://example.invalid/", "https://example.invalid/"); } protected override void NavigateToCore(string uri, bool forceLoad) {} }
class ProbeHandler(CultureService culture) : DelegatingHandler {
 protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
  request.Headers.AcceptLanguage.ParseAdd(culture.CurrentCulture);
  Console.WriteLine("Handler culture=" + culture.CurrentCulture + "; Accept-Language=" + request.Headers.AcceptLanguage);
  return base.SendAsync(request, cancellationToken);
 }
}
class StubHandler : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)); }
