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

    /// <summary>
    /// Скачивание браузером по адресу (подписанной ссылке): файл идёт сразу на диск, без копии в памяти вкладки.
    /// Нужно для больших файлов вроде дампов БД.
    /// </summary>
    public async Task DownloadFromUrlAsync(string fileName, string url) =>
        await js.InvokeVoidAsync("downloadFileFromUrl", fileName, url);
}
