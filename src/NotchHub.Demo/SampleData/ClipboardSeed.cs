using NotchHub.Core.Contracts;
using NotchHub.Demo.Infrastructure;

namespace NotchHub.Demo.SampleData;

/// <summary>Which generated picture a seeded image item shows (paths are filled in once the files exist).</summary>
internal enum SeedImage { None, Screenshot, Photo }

/// <summary>
/// The 23 clipboard history items of the demo (like the reference screenshot: one pinned e-mail, today's chat message,
/// Amazon link, npm command, 1920×1080 image, gold color… plus yesterday's and older items of every kind).
/// </summary>
internal static class ClipboardSeed
{
    public static IReadOnlyList<(ClipItem Item, SeedImage Image)> Create(DateTimeOffset now)
    {
        var random = DemoRandom.For("clipboard");
        var items = new List<(ClipItem, SeedImage)>();

        void Add(ClipKind kind, string? text, TimeSpan ago, string? source, ClipOrigin origin = ClipOrigin.Local,
            bool pinned = false, IReadOnlyList<string>? files = null, SeedImage image = SeedImage.None, int width = 0, int height = 0)
        {
            var bytes = new byte[16];
            random.NextBytes(bytes);
            items.Add((new ClipItem
            {
                Id = new Guid(bytes),
                Kind = kind,
                Text = files is null ? text : string.Join("\n", files),
                Files = files,
                ImageWidth = width,
                ImageHeight = height,
                CopiedAt = now - ago,
                IsPinned = pinned,
                SourceApp = source,
                Origin = origin,
            }, image));
        }

        static TimeSpan H(double hours) => TimeSpan.FromHours(hours);
        static TimeSpan D(double days, double hours = 0) => TimeSpan.FromDays(days) + TimeSpan.FromHours(hours);

        // Pinned.
        Add(ClipKind.Text, "mainak@example.com", D(25, 3), "Microsoft Outlook", pinned: true);

        // Today.
        Add(ClipKind.Text, "Hey Arsh, what's up? How about we go get some coffee at 10 in the morning, and then hang out at the park for a bit?",
            TimeSpan.FromSeconds(25), "WhatsApp");
        Add(ClipKind.Link, "https://www.amazon.in/s?k=red+boxing+gloves", TimeSpan.FromMinutes(4), "Google Chrome");
        Add(ClipKind.Code, "npm run dev -- --port 3000", TimeSpan.FromMinutes(62), "Windows Terminal");
        Add(ClipKind.Image, null, H(2), "Snipping Tool", image: SeedImage.Screenshot, width: 1920, height: 1080);
        Add(ClipKind.Color, "#D4AF5A", H(2.6), "Visual Studio Code");
        Add(ClipKind.Text, "ul. Mokotowska 12/4, 00-561 Warszawa", H(3.4), "Pixel 8 Pro", ClipOrigin.Phone);
        Add(ClipKind.Files, null, H(4.2), "File Explorer", files: [@"C:\Users\Demo\Documents\Faktura FV-2026-09-118.pdf"]);

        // Yesterday.
        Add(ClipKind.Code, "public sealed record TodoItem(Guid Id, string Text, bool IsDone);\n\nvar done = items.Count(i => i.IsDone);",
            D(1, 1), "Visual Studio Code");
        Add(ClipKind.Link, "https://github.com/KDE/kdeconnect-kde", D(1, 2), "Google Chrome");
        Add(ClipKind.Text, "Spotkanie przeniesione na czwartek 14:00, sala 3B.", D(1, 3), "Microsoft Teams");
        Add(ClipKind.Image, null, D(1, 4), "Photos", image: SeedImage.Photo, width: 1280, height: 720);
        Add(ClipKind.Color, "#7C4DFF", D(1, 5), "Figma");
        Add(ClipKind.Files, null, D(1, 6), "File Explorer", files:
        [
            @"C:\Users\Demo\Pictures\Screenshots\Zrzut ekranu 2026-10-11 181203.png",
            @"C:\Users\Demo\Pictures\Screenshots\Zrzut ekranu 2026-10-11 181240.png",
        ]);

        // Older.
        Add(ClipKind.Text, "Dzięki, odezwę się jutro rano!", D(2, 2), "Messenger");
        Add(ClipKind.Link, "https://open-meteo.com/en/docs", D(3, 1), "Google Chrome");
        Add(ClipKind.Code, "dotnet build src/NotchHub.App/NotchHub.App.csproj -c Release", D(4, 5), "Windows Terminal");
        Add(ClipKind.Text, "Lista zakupów: mleko, jajka, chleb, kawa, banany", D(6), "Notepad");
        Add(ClipKind.Color, "#4285F4", D(8, 2), "Microsoft Edge");
        Add(ClipKind.Link, "https://m3.material.io/blog/building-with-m3-expressive", D(12, 4), "Google Chrome");
        Add(ClipKind.Text, "Zadzwoń do mamy po 18:00", D(14, 6), "Pixel 8 Pro", ClipOrigin.Phone);
        Add(ClipKind.Code, "winget upgrade --all --silent", D(18, 1), "Windows Terminal");
        Add(ClipKind.Text, "Material 3 Expressive: tonal palettes, shape morphing, spring motion.", D(21, 3), "Obsidian");

        return items.OrderByDescending(i => i.Item1.CopiedAt).ToArray();
    }
}
