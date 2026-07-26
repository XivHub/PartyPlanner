using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace PartyPlanner.Helpers;

/// <summary>
/// Downloads and caches attachment images from partake.gg CDN as GPU textures.
/// Uses ImageSharp to decode (supports WebP, GIF, PNG, JPEG, etc.) then uploads
/// raw RGBA8 pixels via ITextureProvider.CreateFromRawAsync.
/// </summary>
public sealed class AttachmentImageCache : IDisposable
{
    // DXGI_FORMAT_R8G8B8A8_UNORM = 28
    private const int DxgiFormatR8G8B8A8Unorm = 28;

    /// <summary>Textures kept resident. Attachments are full-size, so this bounds VRAM use.</summary>
    private const int MaxEntries = 64;

    private enum LoadState { Pending, Loaded, Failed }

    private sealed class Entry
    {
        public LoadState State = LoadState.Pending;
        public IDalamudTextureWrap? Texture;
        public long LastUsedFrame;
    }

    private readonly Dictionary<string, Entry> _cache = new();
    private readonly object _lock = new();
    private readonly HttpClient _http;
    private readonly ITextureProvider _textureProvider;
    private readonly CancellationTokenSource _cts = new();
    private long _frame;

    public AttachmentImageCache(ITextureProvider textureProvider)
    {
        _textureProvider = textureProvider;
        _http = new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Dalamud-PartyPlanner");
    }

    /// <summary>Marks the start of a UI frame, used to age cache entries.</summary>
    public void BeginFrame() => Interlocked.Increment(ref _frame);

    /// <summary>
    /// Returns the loaded texture for the given URL, or null if still loading or failed.
    /// Kicks off a download if this URL hasn't been seen before.
    /// </summary>
    public IDalamudTextureWrap? TryGet(string url)
    {
        lock (_lock)
        {
            var frame = Interlocked.Read(ref _frame);

            if (_cache.TryGetValue(url, out var entry))
            {
                entry.LastUsedFrame = frame;
                return entry.State == LoadState.Loaded ? entry.Texture : null;
            }

            var newEntry = new Entry { LastUsedFrame = frame };
            _cache[url] = newEntry;
            _ = LoadAsync(url, newEntry);
            EvictIfNeeded(frame);
            return null;
        }
    }

    /// <summary>
    /// Drops the least recently used entries once the cache exceeds <see cref="MaxEntries"/>.
    /// Only entries untouched this frame are considered, so a texture already handed to the
    /// current draw list is never disposed underneath it.
    /// </summary>
    private void EvictIfNeeded(long frame)
    {
        while (_cache.Count > MaxEntries)
        {
            string? oldestUrl = null;
            Entry? oldest = null;

            foreach (var (candidateUrl, candidate) in _cache)
            {
                if (candidate.LastUsedFrame >= frame || candidate.State == LoadState.Pending)
                    continue;
                if (oldest == null || candidate.LastUsedFrame < oldest.LastUsedFrame)
                {
                    oldestUrl = candidateUrl;
                    oldest = candidate;
                }
            }

            if (oldestUrl == null) return;

            oldest!.Texture?.Dispose();
            _cache.Remove(oldestUrl);
        }
    }

    private async Task LoadAsync(string url, Entry entry)
    {
        try
        {
            var bytes = await _http.GetByteArrayAsync(url, _cts.Token).ConfigureAwait(false);

            // Decode with ImageSharp (supports WebP, GIF, PNG, JPEG, etc.)
            // and convert to raw RGBA8 pixels for Dalamud's texture API.
            byte[] rgba;
            int width, height;
            using (var image = await Task.Run(() => Image.Load<Rgba32>(bytes), _cts.Token).ConfigureAwait(false))
            {
                width = image.Width;
                height = image.Height;
                rgba = new byte[width * height * 4];
                image.CopyPixelDataTo(rgba);
            }

            var spec = new RawImageSpecification(width, height, DxgiFormatR8G8B8A8Unorm, width * 4);
            var tex = await _textureProvider.CreateFromRawAsync(spec, (ReadOnlyMemory<byte>)rgba, url, _cts.Token).ConfigureAwait(false);

            lock (_lock)
            {
                entry.Texture = tex;
                entry.State = LoadState.Loaded;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Plugin.Logger.Warning(ex, "Failed to load attachment image: {0}", url);
            lock (_lock) { entry.State = LoadState.Failed; }
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            foreach (var entry in _cache.Values)
                entry.Texture?.Dispose();
            _cache.Clear();
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        Clear();
        _http.Dispose();
    }
}
