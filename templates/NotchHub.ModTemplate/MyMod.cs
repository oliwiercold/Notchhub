using System.Windows;
using System.Windows.Controls;
using NotchHub.Core;
using NotchHub.UI;
using NotchHub.UI.Controls;
using NotchHub.UI.Localization;
using NotchHub.UI.Modding;

namespace MyMod;

/// <summary>
/// Minimal NotchHub mod: one tab, one quick-settings tile and a HUD. See docs/MODDING.md.
/// Everything here runs on the UI thread. Keep idle CPU at zero: no timers while nothing is visible/running.
/// </summary>
public sealed class MyMod : INotchMod
{
    private IModContext _ctx = null!;
    private ModTile _tile = null!;
    private int _clicks;

    private static string T(string pl, string en) => Loc.Language == "en" ? en : pl;

    public void Initialize(IModContext context)
    {
        _ctx = context;
        _clicks = context.GetSetting("clicks", 0);

        context.AddTab(new ModTab
        {
            Id = "my-mod",                 // unique across all mods
            Title = T("Mój mod", "My mod"),
            Icon = "extension",            // Material Symbols name
            CreateView = () => new MyView(this),
        });

        _tile = new ModTile
        {
            Id = "my-mod.tile",
            Title = T("Mój mod", "My mod"),
            Icon = "touch_app",
            OnClick = Click,
            OnChevron = () => context.OpenTab("my-mod"),
        };
        context.AddTile(_tile);
        Update();
    }

    public void Shutdown() { }

    public int Clicks => _clicks;
    public event EventHandler? Changed;

    public void Click()
    {
        _clicks++;
        _ctx.SetSetting("clicks", _clicks);
        if (_clicks % 10 == 0) _ctx.ShowHud(T($"{_clicks} kliknięć!", $"{_clicks} clicks!"));
        Update();
    }

    private void Update()
    {
        _tile.Subtitle = T($"Kliknięcia: {_clicks}", $"Clicks: {_clicks}");
        _tile.IsActive = _clicks > 0;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The tab view. ITabView.OnShown/OnHidden tell you when to start/stop live work.
    /// Colors come from the theme: SetResourceReference(..., "Brush.X") or {DynamicResource Brush.X} in XAML.
    /// </summary>
    private sealed class MyView : UserControl, ITabView
    {
        private readonly MyMod _mod;
        private readonly TextBlock _count;

        public MyView(MyMod mod)
        {
            _mod = mod;
            SetResourceReference(ForegroundProperty, "Brush.OnSurface");

            var title = new TextBlock { Text = T("Mój mod", "My mod") };
            title.SetResourceReference(StyleProperty, "Text.TitleL");

            _count = new TextBlock { Margin = new Thickness(0, 8, 0, 16) };
            _count.SetResourceReference(StyleProperty, "Text.Headline");

            var button = new M3Button { Content = T("Kliknij", "Click"), Icon = "add", Variant = ButtonVariant.Filled, HorizontalAlignment = HorizontalAlignment.Left };
            button.Click += (_, _) => _mod.Click();

            var card = new Border { Child = new StackPanel { Children = { title, _count, button } } };
            card.SetResourceReference(StyleProperty, "Card");
            Content = card;
        }

        public void OnShown()
        {
            _mod.Changed += OnChanged;
            OnChanged(null, EventArgs.Empty);
        }

        public void OnHidden() => _mod.Changed -= OnChanged;

        private void OnChanged(object? sender, EventArgs e) => _count.Text = _mod.Clicks.ToString();
    }
}
