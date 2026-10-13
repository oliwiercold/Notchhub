using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.Demo.Imaging;
using NotchHub.Demo.SampleData;

namespace NotchHub.Demo.Fakes;

/// <summary>Demo clipboard history (23 seeded items). The system clipboard is never touched; Paste is only logged.</summary>
public sealed class FakeClipboardService : FakeBackgroundService, IClipboardService
{
    private List<ClipItem> _items;
    private readonly Dictionary<Guid, SeedImage> _images = [];
    private string? _systemText;

    /// <summary>Seeds the history relative to now.</summary>
    public FakeClipboardService()
    {
        var seed = ClipboardSeed.Create(DateTimeOffset.Now);
        _items = seed.Select(s => s.Item).ToList();
        foreach (var (item, image) in seed)
            if (image != SeedImage.None) _images[item.Id] = image;
        _systemText = _items.FirstOrDefault(i => i.Kind != ClipKind.Image && !i.IsPinned)?.Text;
    }

    /// <inheritdoc />
    public IReadOnlyList<ClipItem> Items
    {
        get { lock (Gate) return _items.ToArray(); }
    }

    /// <inheritdoc />
    public bool IsMonitoring => true;

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    protected override async Task OnStartAsync(CancellationToken cancellationToken)
    {
        var screenshot = await DemoAssets.ScreenshotClipAsync().ConfigureAwait(false);
        var photo = await DemoAssets.PhotoClipAsync().ConfigureAwait(false);
        lock (Gate)
        {
            _items = _items.Select(i =>
            {
                if (!_images.TryGetValue(i.Id, out var kind)) return i;
                var files = kind == SeedImage.Screenshot ? screenshot : photo;
                return files is null ? i : i with { ImagePath = files.ImagePath, ThumbnailPath = files.ThumbnailPath, ImageWidth = files.Width, ImageHeight = files.Height };
            }).ToList();
        }
        Raise(Changed);
    }

    /// <inheritdoc />
    public void SetPinned(Guid id, bool pinned) =>
        Mutate(list =>
        {
            var index = list.FindIndex(i => i.Id == id);
            if (index < 0 || list[index].IsPinned == pinned) return false;
            list[index] = list[index] with { IsPinned = pinned };
            return true;
        });

    /// <inheritdoc />
    public void Delete(Guid id) => Delete([id]);

    /// <inheritdoc />
    public void Delete(IReadOnlyList<Guid> ids) => Mutate(list => list.RemoveAll(i => ids.Contains(i.Id)) > 0);

    /// <inheritdoc />
    public void Clear(bool keepPinned = true) => Mutate(list => list.RemoveAll(i => !keepPinned || !i.IsPinned) > 0);

    /// <inheritdoc />
    public Task<bool> CopyToClipboardAsync(IReadOnlyList<Guid> ids)
    {
        var ok = false;
        Mutate(list =>
        {
            var picked = ids.Select(id => list.Find(i => i.Id == id)).OfType<ClipItem>().ToList();
            if (picked.Count == 0) return false;
            ok = true;
            _systemText = string.Join("\n", picked.Select(i => i.Text).OfType<string>());
            var now = DateTimeOffset.Now;
            foreach (var item in picked.AsEnumerable().Reverse())
            {
                list.Remove(item);
                list.Insert(0, item with { CopiedAt = now });
            }
            return true;
        });
        return Task.FromResult(ok);
    }

    /// <inheritdoc />
    public async Task<bool> PasteAsync(IReadOnlyList<Guid> ids)
    {
        var ok = await CopyToClipboardAsync(ids).ConfigureAwait(false);
        Log.Info($"DEMO: would paste {ids.Count} item(s) into Visual Studio Code");
        AppServices.Shell?.Collapse();
        return ok;
    }

    /// <inheritdoc />
    public void AddExternal(string text, ClipOrigin origin, bool setSystemClipboard)
    {
        if (string.IsNullOrEmpty(text)) return;
        Mutate(list =>
        {
            list.RemoveAll(i => !i.IsPinned && i.Text == text && i.Kind != ClipKind.Files);
            list.Insert(0, new ClipItem
            {
                Id = Guid.NewGuid(),
                Kind = ClipClassifier.Classify(text),
                Text = text,
                CopiedAt = DateTimeOffset.Now,
                SourceApp = origin == ClipOrigin.Phone ? "Pixel 8 Pro" : "Visual Studio Code",
                Origin = origin,
            });
            if (setSystemClipboard) _systemText = text;
            return true;
        });
    }

    /// <inheritdoc />
    public Task<string?> GetSystemClipboardTextAsync()
    {
        lock (Gate) return Task.FromResult(_systemText);
    }

    private void Mutate(Func<List<ClipItem>, bool> change)
    {
        bool changed;
        lock (Gate) changed = change(_items);
        if (changed) Raise(Changed);
    }
}
