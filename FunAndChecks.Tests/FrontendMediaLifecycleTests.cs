using System.Reflection;
using System.Net;
using FluentAssertions;
using Frontend.Shared.Profiles;
using Frontend.Shared.Resources;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;
using NSubstitute;
using Xunit;
using ProfileMediaComponent = Frontend.Shared.Profiles.ProfileMedia;

namespace FunAndChecks.Tests;

public class FrontendMediaLifecycleTests
{
    [Fact]
    public async Task Editor_WhenSessionClearsDuringCropConfiguration_DoesNotConfigureRemovedMedia()
    {
        var module = new RecordingModule("configureCrop");
        var editor = CreateEditor(module);
        var rendering = editor.AfterRenderAsync();
        await module.PendingCall.WaitAsync(TimeSpan.FromSeconds(5));

        await InvokePrivateAsync(editor, "ClearSessionAsync");
        module.Resume();
        var exception = await Record.ExceptionAsync(() => rendering.WaitAsync(TimeSpan.FromSeconds(5)));

        try
        {
            exception.Should().BeNull();
            module.Calls.Should().Contain("configureCrop");
            module.Calls.Should().NotContain(["attachTimeline", "configureTimeline", "loopSegment"]);
        }
        finally { await editor.DisposeAsync(); }
    }

    [Fact]
    public async Task Editor_WhenDisposedDuringCropConfiguration_DoesNotContinueInterop()
    {
        var module = new RecordingModule("configureCrop");
        var editor = CreateEditor(module);
        var rendering = editor.AfterRenderAsync();
        await module.PendingCall.WaitAsync(TimeSpan.FromSeconds(5));

        await editor.DisposeAsync();
        module.Resume();
        var exception = await Record.ExceptionAsync(() => rendering.WaitAsync(TimeSpan.FromSeconds(5)));

        exception.Should().BeNull();
        module.Disposals.Should().Be(1);
        module.Calls.Should().NotContain(["attachTimeline", "configureTimeline", "loopSegment"]);
    }

    [Fact]
    public async Task Editor_WhenImportCompletesAfterDisposal_DisposesModuleWithoutAttaching()
    {
        var runtime = new PendingImportRuntime();
        var module = new RecordingModule();
        var editor = new TestMediaEditor();
        SetProperty(editor, "JS", runtime);
        var rendering = editor.AfterRenderAsync();
        await runtime.ImportStarted.WaitAsync(TimeSpan.FromSeconds(5));

        var disposing = editor.DisposeAsync().AsTask();
        runtime.Complete(module);
        await disposing.WaitAsync(TimeSpan.FromSeconds(5));
        var exception = await Record.ExceptionAsync(() => rendering.WaitAsync(TimeSpan.FromSeconds(5)));

        exception.Should().BeNull();
        module.Calls.Should().BeEmpty();
        module.Disposals.Should().Be(1);
    }

    [Fact]
    public async Task SelectionPreview_Animated_LoadsSessionPreviewAndPreservesCropAndClip()
    {
        var module = new PreviewModule();
        var handler = new MediaResponseHandler();
        var selection = new MediaSelection(Guid.NewGuid(), new(.25, .125, .5, .25, 1, 4, 2, "Animated"));
        var preview = CreateSelectionPreview(selection, module, handler);

        await preview.ParametersSetAsync();
        await preview.AfterRenderAsync();

        try
        {
            handler.Requests.Should().Equal($"/api/media/editor-sessions/{selection.SessionId}/preview?poster=false");
            GetField(preview, "_url").Should().Be("blob:preview-1");
            GetProperty(preview, "CropStyle").Should().Be("width:200%;height:400%;left:-50%;top:-50%;right:auto;bottom:auto;object-fit:fill;max-width:none");
            module.Calls.Single(c => c.Identifier == "objectUrl").Args[1].Should().Be("video/mp4");
            module.Calls.Single(c => c.Identifier == "loopSegment").Args.Skip(1).Should().Equal(1d, 4d, false);
        }
        finally { await preview.DisposeAsync(); }

        module.ReleasedUrls.Should().Equal("blob:preview-1");
        module.Disposals.Should().Be(1);
    }

    [Fact]
    public async Task SelectionPreview_Still_LoadsSelectedFrameAsImage()
    {
        var module = new PreviewModule();
        var handler = new MediaResponseHandler();
        var selection = new MediaSelection(Guid.NewGuid(), new(0, 0, 1, 1, 0, 5, 2.75, "Still"));
        var preview = CreateSelectionPreview(selection, module, handler);

        await preview.ParametersSetAsync();
        await preview.AfterRenderAsync();

        try
        {
            handler.Requests.Should().Equal($"/api/media/editor-sessions/{selection.SessionId}/frame?timeSeconds=2.75");
            GetField(preview, "_url").Should().Be("blob:preview-1");
            module.Calls.Single(c => c.Identifier == "objectUrl").Args[1].Should().Be("image/webp");
            module.Calls.Should().NotContain(c => c.Identifier == "loopSegment");
        }
        finally { await preview.DisposeAsync(); }

        module.ReleasedUrls.Should().Equal("blob:preview-1");
    }

    [Fact]
    public async Task SelectionPreview_WhenOlderUrlArrivesLate_PreservesNewPreviewAndReleasesBoth()
    {
        var module = new PreviewModule(pauseFirstUrl: true);
        var handler = new MediaResponseHandler();
        var firstSelection = new MediaSelection(Guid.NewGuid(), new(0, 0, 1, 1, 0, 5, 1, "Still"));
        var preview = CreateSelectionPreview(firstSelection, module, handler);
        var firstLoading = preview.ParametersSetAsync();
        await module.FirstUrlStarted.WaitAsync(TimeSpan.FromSeconds(5));

        var currentSelection = firstSelection with { Media = firstSelection.Media with { FrameSeconds = 3 } };
        SetProperty(preview, "Selection", currentSelection);
        await preview.ParametersSetAsync();
        module.ResumeFirstUrl();
        await firstLoading.WaitAsync(TimeSpan.FromSeconds(5));

        try
        {
            GetField(preview, "_url").Should().Be("blob:preview-2");
            GetField(preview, "_loaded").Should().Be(currentSelection);
            module.ReleasedUrls.Should().Equal("blob:preview-1");
        }
        finally { await preview.DisposeAsync(); }

        module.ReleasedUrls.Should().Equal("blob:preview-1", "blob:preview-2");
        module.Disposals.Should().Be(1);
    }

    [Fact]
    public async Task SelectionPreview_WhenDisposedDuringObjectUrlCreation_ReleasesLateUrlBeforeModule()
    {
        var module = new PreviewModule(pauseFirstUrl: true);
        var handler = new MediaResponseHandler();
        var selection = new MediaSelection(Guid.NewGuid(), new(0, 0, 1, 1, 0, 5, 1, "Still"));
        var preview = CreateSelectionPreview(selection, module, handler);
        var loading = preview.ParametersSetAsync();
        await module.FirstUrlStarted.WaitAsync(TimeSpan.FromSeconds(5));

        var disposing = preview.DisposeAsync().AsTask();
        var prematureDisposals = module.Disposals;
        module.ResumeFirstUrl();
        await Task.WhenAll(loading, disposing).WaitAsync(TimeSpan.FromSeconds(5));

        prematureDisposals.Should().Be(0);
        module.Calls.Select(c => c.Identifier).Should().Equal("objectUrl", "release", "dispose");
        module.ReleasedUrls.Should().Equal("blob:preview-1");
        module.Disposals.Should().Be(1);
        GetField(preview, "_url").Should().BeNull();
    }

    [Fact]
    public async Task Profile_WhenOldLeaseFinishesReleasing_DoesNotClearNewPreview()
    {
        var releaseStarted = NewCompletion();
        var releaseCompleted = NewCompletion();
        var profile = new ProfileMediaComponent();
        var oldLease = new MediaBlobLease("blob:old", async () =>
        {
            releaseStarted.TrySetResult();
            await releaseCompleted.Task;
        });
        SetField(profile, "_blob", oldLease);
        SetField(profile, "_url", oldLease.Url);
        var releasing = InvokePrivateAsync(profile, "ReleaseAsync");
        await releaseStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var newId = Guid.NewGuid();
        var newLease = new MediaBlobLease("blob:new", () => ValueTask.CompletedTask);
        var newPoster = new MediaBlobLease("blob:new-poster", () => ValueTask.CompletedTask);
        SetField(profile, "_blob", newLease);
        SetField(profile, "_posterBlob", newPoster);
        SetField(profile, "_url", newLease.Url);
        SetField(profile, "_poster", newPoster.Url);
        SetField(profile, "_loaded", newId);
        releaseCompleted.TrySetResult();
        await releasing.WaitAsync(TimeSpan.FromSeconds(5));

        try
        {
            GetField(profile, "_blob").Should().BeSameAs(newLease);
            GetField(profile, "_posterBlob").Should().BeSameAs(newPoster);
            GetField(profile, "_url").Should().Be(newLease.Url);
            GetField(profile, "_poster").Should().Be(newPoster.Url);
            GetField(profile, "_loaded").Should().Be(newId);
        }
        finally
        {
            await InvokePrivateAsync(profile, "ReleaseAsync");
            await profile.DisposeAsync();
        }
    }

    private static TestMediaEditor CreateEditor(RecordingModule module)
    {
        var editor = new TestMediaEditor();
        SetField(editor, "_module", module);
        SetField(editor, "_session", new MediaSessionDto(Guid.NewGuid(), 640, 480, 5, "Video", true, DateTime.UtcNow.AddMinutes(5)));
        SetField(editor, "_configurePreview", true);
        // The session has already been consumed, so cleanup does not call the API.
        SetField(editor, "_consumed", true);
        return editor;
    }

    private static TestSelectionPreview CreateSelectionPreview(MediaSelection selection, PreviewModule module, MediaResponseHandler handler)
    {
        var runtime = new PendingImportRuntime();
        runtime.Complete(module);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var preview = new TestSelectionPreview();
        SetProperty(preview, "Selection", selection);
        SetProperty(preview, "Api", new ProfileApi(http, Substitute.For<IStringLocalizer<AppStrings>>()));
        SetProperty(preview, "JS", runtime);
        return preview;
    }

    private static Type ComponentType(object component) => component switch
    {
        MediaEditor => typeof(MediaEditor),
        MediaSelectionPreview => typeof(MediaSelectionPreview),
        _ => typeof(ProfileMediaComponent)
    };
    private static FieldInfo Field(object component, string name) => ComponentType(component).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static void SetField(object component, string name, object? value) => Field(component, name).SetValue(component, value);
    private static object? GetField(object component, string name) => Field(component, name).GetValue(component);
    private static void SetProperty(object component, string name, object value) =>
        ComponentType(component).GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(component, value);
    private static object? GetProperty(object component, string name) =>
        ComponentType(component).GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(component);
    private static Task InvokePrivateAsync(object component, string name) =>
        (Task)ComponentType(component).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, null)!;
    private static TaskCompletionSource NewCompletion() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class TestMediaEditor : MediaEditor
    {
        public Task AfterRenderAsync() => base.OnAfterRenderAsync(false);
    }

    private sealed class TestSelectionPreview : MediaSelectionPreview
    {
        public Task ParametersSetAsync() => base.OnParametersSetAsync();
        public Task AfterRenderAsync() => base.OnAfterRenderAsync(false);
    }

    private sealed class MediaResponseHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.PathAndQuery);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });
        }
    }

    private sealed class PreviewModule(bool pauseFirstUrl = false) : IJSObjectReference
    {
        private readonly TaskCompletionSource _firstStarted = NewCompletion();
        private readonly TaskCompletionSource _firstResumed = NewCompletion();
        private int _urlCount;
        public List<(string Identifier, object?[] Args)> Calls { get; } = [];
        public IEnumerable<string> ReleasedUrls => Calls.Where(c => c.Identifier == "release").Select(c => (string)c.Args[0]!);
        public Task FirstUrlStarted => _firstStarted.Task;
        public int Disposals { get; private set; }
        public void ResumeFirstUrl() => _firstResumed.TrySetResult();
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, CancellationToken.None, args);
        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            Calls.Add((identifier, args ?? []));
            if (identifier != "objectUrl") return default!;
            var number = ++_urlCount;
            if (pauseFirstUrl && number == 1)
            {
                _firstStarted.TrySetResult();
                await _firstResumed.Task;
            }
            return (TValue)(object)$"blob:preview-{number}";
        }
        public ValueTask DisposeAsync()
        {
            Disposals++;
            Calls.Add(("dispose", []));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingModule(string? pendingIdentifier = null) : IJSObjectReference
    {
        private readonly TaskCompletionSource _started = NewCompletion();
        private readonly TaskCompletionSource _resumed = NewCompletion();
        public List<string> Calls { get; } = [];
        public Task PendingCall => _started.Task;
        public int Disposals { get; private set; }
        public void Resume() => _resumed.TrySetResult();
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, CancellationToken.None, args);
        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            Calls.Add(identifier);
            if (identifier == pendingIdentifier)
            {
                _started.TrySetResult();
                await _resumed.Task;
            }
            return default!;
        }
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }

    private sealed class PendingImportRuntime : IJSRuntime
    {
        private readonly TaskCompletionSource _started = NewCompletion();
        private readonly TaskCompletionSource<IJSObjectReference> _module = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task ImportStarted => _started.Task;
        public void Complete(IJSObjectReference module) => _module.TrySetResult(module);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, CancellationToken.None, args);
        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            identifier.Should().Be("import");
            _started.TrySetResult();
            return (TValue)(object)await _module.Task;
        }
    }
}
