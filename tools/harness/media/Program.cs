// Read-only diagnostics for NotchHub.Platform.Media. Never changes volume, mute, default device or playback.
using NotchHub.Platform.Media.Audio;
using NotchHub.Platform.Media.Media;

var watchSeconds = args.Length > 0 && int.TryParse(args[0], out var s) ? s : 3;

using var audio = new AudioService();
await audio.StartAsync();
await Task.Delay(800); // let icons / session snapshot settle
Console.WriteLine($"[audio] available={audio.IsAvailable} volume={audio.Volume:0.00} muted={audio.IsMuted}");
Console.WriteLine($"[audio] default={audio.DefaultOutput?.Name} | {audio.DefaultOutput?.Description} | {audio.DefaultOutput?.Kind}");
foreach (var d in audio.Outputs) Console.WriteLine($"   out: {(d.IsDefault ? "*" : " ")} {d.Name} | {d.Description} | {d.Kind}");
foreach (var a in audio.Sessions)
    Console.WriteLine($"   app: {a.DisplayName} pid={a.ProcessId} vol={a.Volume:0.00} muted={a.IsMuted} icon={(a.IconPng?.Length ?? 0)}B id={a.Id}");

using var media = new MediaService();
await media.StartAsync();
await Task.Delay(1200);
foreach (var m in media.Sessions)
    Console.WriteLine($"[media] {m.SourceAppName} ({m.SourceAppId}) {m.State} '{m.Title}' - '{m.Artist}' pos={m.Position:mm\\:ss}/{m.Duration:mm\\:ss} " +
                      $"at={m.PositionUpdatedAt:HH:mm:ss} thumb={(m.Thumbnail?.Length ?? 0)}B key={m.ThumbnailKey} next={m.CanNext} seek={m.CanSeek} shuffle={m.IsShuffleActive} repeat={m.RepeatMode}");
Console.WriteLine($"[media] current={media.Current?.SourceAppName ?? "(none)"}");

var changes = 0;
media.Changed += (_, _) => Interlocked.Increment(ref changes);
audio.VolumeChanged += (_, e) => Console.WriteLine($"[audio] volume event {e.Volume:0.00} muted={e.IsMuted} external={e.IsExternal}");
await Task.Delay(TimeSpan.FromSeconds(watchSeconds));
Console.WriteLine($"[media] Changed events during {watchSeconds}s idle watch: {changes}");
