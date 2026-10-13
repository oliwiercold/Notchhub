namespace NotchHub.Core.Settings;

public interface ISettingsService
{
    /// <summary>The live settings object. Treat as read-only outside <see cref="Update"/>.</summary>
    AppSettings Current { get; }

    /// <summary>Raised (on the calling thread) after every <see cref="Update"/>/<see cref="Save"/>.</summary>
    event EventHandler? Changed;

    /// <summary>Mutates the settings, persists them and raises <see cref="Changed"/>.</summary>
    void Update(Action<AppSettings> mutate);

    /// <summary>Persists the current settings and raises <see cref="Changed"/>.</summary>
    void Save();
}

public sealed class SettingsService : ISettingsService
{
    private readonly string _path;
    private readonly object _gate = new();

    public SettingsService() : this(AppPaths.File("settings.json")) { }

    public SettingsService(string path)
    {
        _path = path;
        Current = JsonStore.Load(path, () => new AppSettings());
    }

    public AppSettings Current { get; }

    public event EventHandler? Changed;

    public void Update(Action<AppSettings> mutate)
    {
        lock (_gate) mutate(Current);
        Save();
    }

    public void Save()
    {
        lock (_gate) JsonStore.Save(_path, Current);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
