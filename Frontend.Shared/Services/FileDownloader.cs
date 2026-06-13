using Microsoft.JSInterop;

namespace Frontend.Shared.Services;

/// <summary>Скачивание файла из потока через JS-функцию downloadFileFromStream.</summary>
public class FileDownloader(IJSRuntime js)
{
    public async Task DownloadAsync(string fileName, Stream stream)
    {
        using var streamRef = new DotNetStreamReference(stream);
        await js.InvokeVoidAsync("downloadFileFromStream", fileName, streamRef);
    }
}
