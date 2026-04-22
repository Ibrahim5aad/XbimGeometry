using Microsoft.JSInterop;

namespace Xbim.Geometry.Viewer;

/// <summary>
/// Manages the JavaScript interop bridge to the Three.js viewer module.
/// </summary>
internal sealed class XbimViewerInterop : IAsyncDisposable
{
    private readonly Lazy<Task<IJSObjectReference>> _moduleTask;

    public XbimViewerInterop(IJSRuntime jsRuntime)
    {
        _moduleTask = new(() => jsRuntime.InvokeAsync<IJSObjectReference>(
            "import", "./_content/Xbim.Geometry.Viewer/js/xbimViewerInterop.js").AsTask());
    }

    private async ValueTask<IJSObjectReference> ModuleAsync()
        => await _moduleTask.Value;

    public async ValueTask<string> InitViewerAsync(string canvasId)
    {
        var module = await ModuleAsync();
        return await module.InvokeAsync<string>("initViewer", canvasId);
    }

    public async ValueTask LoadWexBimAsync(string viewerId, DotNetStreamReference streamRef, int modelId)
    {
        var module = await ModuleAsync();
        await module.InvokeVoidAsync("loadWexBim", viewerId, streamRef, modelId);
    }

    public async ValueTask LoadWexBimFromUrlAsync(string viewerId, string url, int modelId)
    {
        var module = await ModuleAsync();
        await module.InvokeVoidAsync("loadWexBimFromUrl", viewerId, url, modelId);
    }

    public async ValueTask ClearSceneAsync(string viewerId)
    {
        var module = await ModuleAsync();
        await module.InvokeVoidAsync("clearScene", viewerId);
    }

    public async ValueTask SetBackgroundColorAsync(string viewerId, string color)
    {
        var module = await ModuleAsync();
        await module.InvokeVoidAsync("setBackgroundColor", viewerId, color);
    }

    public async ValueTask FitAllAsync(string viewerId)
    {
        var module = await ModuleAsync();
        await module.InvokeVoidAsync("fitAll", viewerId);
    }

    public async ValueTask SetProjectionAsync(string viewerId, bool orthographic)
    {
        var module = await ModuleAsync();
        await module.InvokeVoidAsync("setProjection", viewerId, orthographic);
    }

    public async ValueTask<string> GetProjectionAsync(string viewerId)
    {
        var module = await ModuleAsync();
        return await module.InvokeAsync<string>("getProjection", viewerId);
    }

    public async ValueTask SetModelVisibleAsync(string viewerId, int modelId, bool visible)
    {
        var module = await ModuleAsync();
        await module.InvokeVoidAsync("setModelVisible", viewerId, modelId, visible);
    }

    public async ValueTask RemoveModelAsync(string viewerId, int modelId)
    {
        var module = await ModuleAsync();
        await module.InvokeVoidAsync("removeModel", viewerId, modelId);
    }

    // ── Streaming API ──

    public async ValueTask StreamBeginAsync(string viewerId, int modelId)
    {
        var module = await ModuleAsync();
        await module.InvokeVoidAsync("streamBegin", viewerId, modelId);
    }

    public async ValueTask StreamHeaderAsync(string viewerId, float oneMeter,
        float bx, float by, float bz, float sx, float sy, float sz, int productCount)
    {
        var module = await ModuleAsync();
        await module.InvokeVoidAsync("streamHeader", viewerId,
            oneMeter, bx, by, bz, sx, sy, sz, productCount);
    }

    public async ValueTask StreamStyleAsync(string viewerId, int id, float r, float g, float b, float a)
    {
        var module = await ModuleAsync();
        await module.InvokeVoidAsync("streamStyle", viewerId, id, r, g, b, a);
    }

    public async ValueTask StreamGeometryAsync(string viewerId, int id, DotNetStreamReference streamRef)
    {
        var module = await ModuleAsync();
        await module.InvokeVoidAsync("streamGeometry", viewerId, id, streamRef);
    }

    public async ValueTask StreamInstanceAsync(string viewerId, int productLabel, int typeId,
        int geometryId, int styleId, double[] transform,
        float bx, float by, float bz, float sx, float sy, float sz)
    {
        var module = await ModuleAsync();
        await module.InvokeVoidAsync("streamInstance", viewerId,
            productLabel, typeId, geometryId, styleId, transform,
            bx, by, bz, sx, sy, sz);
    }

    public async ValueTask StreamCompleteAsync(string viewerId)
    {
        var module = await ModuleAsync();
        await module.InvokeVoidAsync("streamComplete", viewerId);
    }

    public async ValueTask DisposeViewerAsync(string viewerId)
    {
        var module = await ModuleAsync();
        await module.InvokeVoidAsync("dispose", viewerId);
    }

    public async ValueTask DisposeAsync()
    {
        if (_moduleTask.IsValueCreated)
        {
            var module = await _moduleTask.Value;
            await module.DisposeAsync();
        }
    }
}
