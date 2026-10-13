# NotchHub — Motion spec (Legacy vs Refreshed)

Status: research + spec for `Appearance.AnimationStyle = Legacy | Refreshed`. Companion to DESIGN.md §4–§5.
Units: DIP, seconds. A spring is written **(response T, damping ratio ζ)**, mass 1, so stiffness k = (2π/T)², damping
coefficient c = 4πζ/T. Overshoot of a step = exp(−πζ/√(1−ζ²)): ζ 0.6 → 9.5 %, 0.7 → 4.6 %, 0.75 → 2.8 %, 0.8 → 1.5 %,
0.85 → 0.6 %, 0.9 → 0.15 %, 1.0 → 0.

## 0. Why the current motion feels "mediocre"

1. **Velocity is lost on every interruption.** `SpringEase` is an easing curve on a fixed-duration `DoubleAnimation`.
   `HandoffBehavior.SnapshotAndReplace` restarts it from rest at the snapshotted value, so hover in→out→in kinks and stalls.
2. **Two motion languages.** The container uses springs, but content uses a 90 ms linear fade-out and a 180 ms `CubicEase`
   fade-in after 60 ms. Tabs work the same way.
3. **Everything moves together.** W, H, Rb and Rs share one spring, so there is no lead/lag (squash & stretch). Collapse reuses
   the bouncy expand spring (ζ 0.8 → about 8 DIP rubber-band on an expanded→collapsed height of ~500 DIP).
4. **Layout runs on every frame.** `BodyWidth`/`BodyHeight`/`ShoulderRadius` are `AffectsMeasure`, and `MeasureOverride` calls
   `Child.Measure(new Size(W,H))` with a new constraint each frame. That re-measures the whole Stage (text included) about 60–144
   times per second. `ArrangeOverride` allocates a new clip `RectangleGeometry` per frame, `OnRender` allocates four `Pen`s per
   frame while the glow shows, and `TabIndicator` animates `WidthProperty`.
5. **Expanded panel uses `DropShadowEffect` (BlurRadius 20).** Every frame of a panel-height change re-blurs about 820×640 px
   and grows the dirty rectangle of the layered window (see §3).

## 1. Reference research

### 1.1 Apple Dynamic Island
* Apple does not publish the island's spring values. The documented model is the SwiftUI spring with a **perceptual duration +
  bounce**: k = (2π/duration)², ζ = 1 − bounce. Guidance: bounce 0 is the versatile default, ~0.15 gives a brisk tail, ~0.3 is
  noticeably bouncy, avoid > ~0.4 for UI. A retargeted spring starts toward the new target with the velocity it had, and
  completion should key off the perceptual duration, not the settle time
  ([WWDC23 *Animate with springs*](https://developer.apple.com/videos/play/wwdc2023/10158/)). SwiftUI presets (all 0.5 s):
  `.smooth` bounce 0, `.snappy` ≈ 0.15, `.bouncy` ≈ 0.3.
* Design intent ([WWDC23 *Design dynamic Live Activities*](https://developer.apple.com/videos/play/wwdc2023/10194/)): the island
  is meant to feel like a living organism with a "deliberate elasticity". It shape-shifts organically and blooms from Compact
  into Expanded; elements keep their relative placement between sizes; shapes are concentric, with even inner margins.
* Observed behavior ([iPhoneSoft summary of @xezrunner's slow-motion comparisons](https://iphonesoft.fr/2025/03/16/qualite-animations-dynamic-island-baisse-deux-ans)):
  the iOS 16 island breathed (a subtle pulse) and merged blobs metaball-style. The iOS 17/18 bounce is criticized as
  exaggerated and badly eased. **Lesson: large shapes get small overshoot (ζ ≥ 0.75); only small elements may pop.**
* Content vs. container (the open-source recreations below agree): the container leads, and content enters once the shape is
  already moving (blur → sharp, scale up from the top edge, fade in). On close, content leaves first (fast fade/blur) and the
  shape follows on a firmer, near-critical spring. The width/height lead/lag is what reads as squash & stretch.

### 1.2 Android 16 / Material 3 Expressive
* Compose `MotionScheme` tokens ([Expressive](https://github.com/androidx/androidx/blob/androidx-main/compose/material3/material3/src/commonMain/kotlin/androidx/compose/material3/tokens/ExpressiveMotionTokens.kt),
  [Standard](https://github.com/androidx/androidx/blob/androidx-main/compose/material3/material3/src/commonMain/kotlin/androidx/compose/material3/tokens/StandardMotionTokens.kt)):

| token | Expressive ζ / k (→ T) | Standard ζ / k (→ T) | use |
|---|---|---|---|
| Spatial Fast | 0.6 / 800 (0.22 s) | 0.9 / 1400 (0.17 s) | small moves, indicators, press release |
| Spatial Default | 0.8 / 380 (0.32 s) | 0.9 / 700 (0.24 s) | size, position, shape |
| Spatial Slow | 0.8 / 200 (0.44 s) | 0.9 / 300 (0.36 s) | large entrances |
| Effects Fast / Default / Slow | 1.0 / 3800, 1600, 800 (0.10, 0.16, 0.22 s) | same | opacity, color, blur — **never overshoot** |

  NotchHub's `Springs.*` already use the Expressive values.
* Components (androidx sources):
  * [Button](https://github.com/androidx/androidx/blob/androidx-main/compose/material3/material3/src/commonMain/kotlin/androidx/compose/material3/Button.kt):
    press morphs the shape full → `CornerSmall` (8 dp), deliberately on **DefaultEffects so it never bounces**.
  * [Slider](https://github.com/androidx/androidx/blob/androidx-main/compose/material3/material3/src/commonMain/kotlin/androidx/compose/material3/Slider.kt):
    the thumb **halves its width** while pressed or dragged.
  * [ButtonGroup](https://github.com/androidx/androidx/blob/androidx-main/compose/material3/material3/src/commonMain/kotlin/androidx/compose/material3/ButtonGroup.kt):
    the pressed item widens by `ExpandedRatio = 0.15` and neighbors compress (`FastSpatial`).
* Physics ([SpringForce](https://github.com/androidx/androidx/blob/androidx-main/dynamicanimation/dynamicanimation/src/main/java/androidx/dynamicanimation/animation/SpringForce.java),
  [DynamicAnimation](https://github.com/androidx/androidx/blob/androidx-main/dynamicanimation/dynamicanimation/src/main/java/androidx/dynamicanimation/animation/DynamicAnimation.java)):
  each frame evaluates the **closed-form** solution from (displacement, velocity, dt), and `animateToFinalPosition` keeps the
  velocity. Rest: |x| < 0.75 × min-visible-change and |v| < that × 62.5 /s (min-visible-change: 1 px, alpha 1/256, scale 1/500).

### 1.3 Open-source notch / island apps (code read)

| project | engine / params | what to copy | what to avoid |
|---|---|---|---|
| [boring.notch](https://github.com/TheBoredTeam/boring.notch/blob/main/boringNotch/ContentView.swift) (macOS) | open `spring(response 0.42, ζ 0.8)`, close `spring(0.45, ζ 1.0)`, hover `interactiveSpring(0.38, 0.8)`; content `.scale(0.8, anchor: .top) + .opacity`; radii animated via `AnimatablePair` ([NotchShape](https://github.com/TheBoredTeam/boring.notch/blob/main/boringNotch/components/Notch/NotchShape.swift)) | **asymmetric open/close springs**, top-anchored content scale | 0.8 scale is large for our 820-wide panel |
| [DynamicNotchKit](https://github.com/MrKai77/DynamicNotchKit/blob/main/Sources/DynamicNotchKit/Views/NotchView.swift) | open `.bouncy(0.4)`, close `.smooth(0.4)`, convert `.snappy(0.4)`; expanded content `blur(10) + scale(y 0.6, .top) + opacity`; compact items `scale(x 0)` toward the cutout; radii 6/14 → 15/20; background padded −50 to leave room for overshoot | blur+scale+fade content transition, compact items grow out of the notch | `.bouncy` on the whole panel |
| [NotchDrop](https://github.com/Lakr233/NotchDrop/blob/main/NotchDrop/NotchViewModel.swift) | `interactiveSpring(duration 0.5, extraBounce 0.25)`; content `.scale + .opacity + offset(y −h/2)`; radius closed 8 / hover "popping" 10 (+4 height) / open 32 | hover "pop" state | heavy bounce |
| [DynamicWin](https://github.com/FlorianButz/DynamicWin/blob/babccf2d4753328300697e5f3e151c129ffbc889/DynamicWin/Utils/SecondOrder.cs) (Windows, WPF + SkiaSharp) | second-order dynamics, semi-implicit Euler: f = 3 Hz (k ≈ 355), ζ 0.6 expand / 0.9 contract | stateful integrator keeps velocity; firmer contract | driven by a 14 ms WinForms `Timer` ([RendererMain](https://github.com/FlorianButz/DynamicWin/blob/babccf2d4753328300697e5f3e151c129ffbc889/DynamicWin/Main/RendererMain.cs)), which is not vsync-aligned and judders |
| [cult-ui DynamicIsland](https://github.com/nolly-studio/cult-ui/blob/main/apps/www/registry/default/ui/dynamic-island.tsx) (web) | k 400, c 30 (ζ 0.75, T 0.31); exit `opacity 0, blur 10px, scale 0.95, y 20`; enter `scale 0.9→1, y 5→0` | numbers for blur/scale | — |
| [noctalia-island PR #2](https://github.com/harveywuk/noctalia-island/pull/2) | expand (0.45, ζ 0.75), collapse (0.36, ζ 0.86); content fade-in = T/2; fade-out 150 ms; analytic solver, velocity kept on retarget | the "collapse firmer than expand" rule | — |

Common thread: **springs with state** (velocity survives retargets), **asymmetric expand/collapse**, **content transitions
built from blur + scale (top anchor) + opacity**, and a little bounce on growth only.

## 2. Interruptible physics springs

* **State per channel:** value x, velocity v, target, spring (T, ζ). Retarget changes only the target and the spring;
  x and v carry over, so C¹ continuity is automatic.
* **Integration:** use the **analytic step** on the displacement d = x − target. It is exact for any dt (identical at 60, 144
  or 240 Hz, no stability limit). With ω₀ = 2π/T and ω_d = ω₀√(1−ζ²):
  * underdamped: `d' = e^(−ζω₀dt)·(d·cos + (v+ζω₀d)/ω_d·sin)`, `v' = e^(−ζω₀dt)·(v·cos − (ω₀²d+ζω₀v)/ω_d·sin)`;
  * critical: `d' = e^(−ω₀dt)(d + (v+ω₀d)dt)`, `v' = e^(−ω₀dt)(v − ω₀(v+ω₀d)dt)`; overdamped: two exponentials.

  Semi-implicit Euler is acceptable only with a fixed 1 ms substep accumulator (DynamicWin's k2 clamp shows the stability
  hacks you need without one).
* **Time base:** `RenderingEventArgs.RenderingTime`, the time WPF expects the frame to be presented
  ([source](https://source.dot.net/PresentationCore/System/Windows/Media/RenderingEventArgs.cs.html)). The event can be raised
  more than once per frame (extra layout-driven render passes): **skip it when `RenderingTime` is unchanged**. Clamp dt to
  ≤ 50 ms so a hitch doesn't teleport the shape; use the measured frame period for the first frame after waking.
* **Rest:** |d| < ε and |v| < 62.5·ε (Android's ratio) → snap to target, v = 0, raise `Settled`. ε = 0.1 DIP for geometry,
  1/512 opacity, 1/2000 scale, 0.05 blur radius. At rest also snap geometry and translations to device pixels (multiples of
  1/DpiScale) for crisp text and edges; subpixel values are fine only while moving.
* **Zero idle CPU:** a `CompositionTarget.Rendering` subscription keeps WPF rendering every frame. Subscribe on the first
  enqueue and **unsubscribe as soon as the active list is empty**. Delays are per-channel state, not `DispatcherTimer`s, so they
  cost nothing and cancel naturally on retarget.

```csharp
namespace NotchHub.UI.Animation;

/// Spring as (response T in s, damping ratio ζ); mass 1 ⇒ k = (2π/T)², c = 4πζ/T.
public readonly record struct Spring(double Response, double DampingRatio)
{
    public static Spring FromM3(double stiffness, double ratio) => new(2 * Math.PI / Math.Sqrt(stiffness), ratio);
    public static Spring FromBounce(double duration, double bounce) => new(duration, 1 - bounce); // Apple, bounce ≥ 0
}

/// Owner of one or more channels; called once per frame after its channels stepped (one InvalidateVisual / SetValue batch).
public interface ISpringSink { void OnSpringFrame(); }

public sealed class SpringChannel(double value, double epsilon, ISpringSink sink)
{
    public double Value { get; private set; } = value;
    public double Velocity { get; private set; }          // units/s — survives retargets
    public double Target { get; private set; } = value;
    public Spring Spring { get; private set; } = new(0.3, 1);
    public ISpringSink Sink { get; } = sink;
    public event Action? Settled;                         // e.g. collapse a layer when opacity reaches 0
    internal bool Queued;
    private double _delay, _pendingTarget; private Spring _pendingSpring;

    public void AnimateTo(double target, Spring spring, double delay = 0)
    {
        if (delay > 0) { _delay = delay; _pendingTarget = target; _pendingSpring = spring; }
        else { _delay = 0; Target = target; Spring = spring; }
        SpringAnimator.Enqueue(this);
    }
    public void AddVelocity(double v) { Velocity += v; SpringAnimator.Enqueue(this); } // wheel / drag fling hand-off
    public void Snap(double v) { _delay = 0; Value = Target = v; Velocity = 0; Sink.OnSpringFrame(); }

    internal bool Step(double dt)
    {
        if (_delay > 0) { _delay -= dt; if (_delay > 0) return false; Target = _pendingTarget; Spring = _pendingSpring; }
        double d = Value - Target, v = Velocity, w0 = 2 * Math.PI / Spring.Response, z = Spring.DampingRatio, nd, nv;
        if (z < 0.9999)
        {
            double wd = w0 * Math.Sqrt(1 - z * z), e = Math.Exp(-z * w0 * dt), c = Math.Cos(wd * dt), s = Math.Sin(wd * dt);
            nd = e * (d * c + (v + z * w0 * d) / wd * s);
            nv = e * (v * c - (w0 * w0 * d + z * w0 * v) / wd * s);
        }
        else if (z <= 1.0001)
        {
            double e = Math.Exp(-w0 * dt), b = v + w0 * d;
            nd = e * (d + b * dt); nv = e * (v - w0 * b * dt);
        }
        else
        {
            double r = w0 * Math.Sqrt(z * z - 1), r1 = -z * w0 + r, r2 = -z * w0 - r;
            double c1 = (v - r2 * d) / (r1 - r2), c2 = d - c1, e1 = Math.Exp(r1 * dt), e2 = Math.Exp(r2 * dt);
            nd = c1 * e1 + c2 * e2; nv = c1 * r1 * e1 + c2 * r2 * e2;
        }
        bool rest = Math.Abs(nd) < epsilon && Math.Abs(nv) < epsilon * 62.5;
        Value = rest ? Target : Target + nd;
        Velocity = rest ? 0 : nv;
        return rest;
    }
    internal void RaiseSettled() => Settled?.Invoke();
}

public static class SpringAnimator
{
    private static readonly List<SpringChannel> Active = new(32);
    private static readonly List<ISpringSink> Dirty = new(8);
    private static TimeSpan _last;
    private static bool _hooked;
    public static double FramePeriod { get; set; } = 1 / 60.0;  // from DisplayInfo.RefreshHz (EnumDisplaySettings)
    public static double TimeScale { get; set; } = 1;           // dev slow-mo (NOTCHHUB_MOTION_SCALE=0.2)
    public static bool IsRunning => _hooked;                     // for tests: must be false when idle

    internal static void Enqueue(SpringChannel c)
    {
        if (!c.Queued) { c.Queued = true; Active.Add(c); }
        if (_hooked) return;
        _hooked = true; _last = TimeSpan.Zero;
        CompositionTarget.Rendering += OnRendering;
    }

    private static void OnRendering(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (now == _last) return;                                   // duplicate raise for the same frame
        double dt = _last == TimeSpan.Zero ? FramePeriod : (now - _last).TotalSeconds;
        _last = now;
        dt = Motion.ReduceMotion ? 10 : Math.Clamp(dt, 0, 0.05) * TimeScale;   // ReduceMotion ⇒ land this frame

        for (int i = Active.Count - 1; i >= 0; i--)
        {
            var c = Active[i];
            bool rest = c.Step(dt);
            if (!Dirty.Contains(c.Sink)) Dirty.Add(c.Sink);         // tiny list, no allocation
            if (!rest) continue;
            Active[i] = Active[^1]; Active.RemoveAt(Active.Count - 1); c.Queued = false;
            c.RaiseSettled();                                       // may enqueue more; picked up next frame
        }
        foreach (var s in Dirty) s.OnSpringFrame();
        Dirty.Clear();
        if (Active.Count == 0) { CompositionTarget.Rendering -= OnRendering; _hooked = false; }   // zero idle CPU
    }
}

/// Adapter for any double DP (Opacity, ScaleTransform.ScaleX, BlurEffect.Radius …). One SetValue per frame.
public sealed class DpSink(DependencyObject target, DependencyProperty dp) : ISpringSink
{
    public SpringChannel Channel = null!;
    public void OnSpringFrame() => target.SetValue(dp, Channel.Value);
}
```

Shell usage (Refreshed): `NotchShape : ISpringSink` owns the channels W, H, Rb and Rs, and its `OnSpringFrame` updates the body
drawing and clip once per frame (§3). A morph is just `_w.AnimateTo(...)`, `_h.AnimateTo(...)` with the springs and delays from
§4. `Motion.Channel(element, dp, ε)` keeps one channel per (object, DP) in a `ConditionalWeakTable`, so repeated `Animate()`
calls retarget the same channel and keep its velocity. On first use, read the current animated value, call
`BeginAnimation(dp, null)`, then set it locally; a leftover Legacy timeline would otherwise override every local value.

## 3. WPF-specific smoothness

* **Frame rate.** Under DWM, `MediaContext` sets its animation render rate to the reported display refresh rate (capped at
  1000; [MediaContext.cs](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/Media/MediaContext.cs),
  `NotifyPresented`). `Timeline.DesiredFrameRate` defaults to `null` (system-controlled) and is documented as a *guideline*,
  settable only on root timelines ([docs](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.animation.timeline.desiredframerate)).
  So the "WPF = 60 fps" cap is not in the source for the DWM path. **Verify it on the layered window** with the debug frame
  counter (fps, plus dropped frames where dt > 1.5 × period). If it shows 60 on a 144 Hz panel, call
  `Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(Timeline), new FrameworkPropertyMetadata { DefaultValue = maxHz })`
  once at `App` startup, before any timeline exists. That affects Legacy only. Use the highest rate across monitors, because the
  value is a cap and metadata can't change later. Refreshed doesn't need it: it advances by `RenderingTime` every presented
  frame.
* **Refresh-rate detection.** `MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST)` → `GetMonitorInfo` (`MONITORINFOEX.szDevice`)
  → `EnumDisplaySettings(szDevice, ENUM_CURRENT_SETTINGS, ref DEVMODE).dmDisplayFrequency`. Treat 0 or 1 as "default" (use 60),
  and fall back to `DwmGetCompositionTimingInfo(IntPtr.Zero).rateRefresh` for fractional rates (Chromium's pattern,
  [vsync_provider_win.cc](https://chromium.googlesource.com/chromium/src/+/7a66b24/ui/gl/vsync_provider_win.cc)). Re-query on
  `WM_DISPLAYCHANGE`, `WM_DPICHANGED` and notch-monitor changes. Use the value only for the first-frame dt and diagnostics.
* **Layered-window cost.** `AllowsTransparency` windows are presented by `PresentWithGDI`: `GetDC` on the D3D back buffer for
  the *source rect*, then `UpdateLayeredWindowIndirect` with `prcDirty` = that rect and `ULW_EX_NORESIZE`
  ([d3ddevice.cpp](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/WpfGfx/core/hw/d3ddevice.cpp),
  [oscompat.cpp](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/WpfGfx/core/common/oscompat.cpp)). That is a
  GPU→system-memory readback whose cost **scales with the dirty bounding box**; full-window redraws are the worst case
  ([background](https://learn.microsoft.com/en-us/archive/blogs/dwayneneed/transparent-windows-in-wpf),
  [HW vs SW](https://learn.microsoft.com/en-us/archive/blogs/seema/layered-windows-sw-is-sometimes-faster-than-hw)).
  * Never animate `Window.Width/Height/Left/Top` (resizing a layered window repaints all of it); keep the fixed 1000×660 window.
  * Keep effects out of large morphing areas: an effect inflates the dirty rect by its radius and re-rasterizes every frame.
  * The idle collapsed notch costs nothing: nothing dirty means no `UpdateLayeredWindow` calls.
* **No layout during animation.**
  * Make `NotchShape`'s geometry DPs `AffectsRender` only. `MeasureOverride` returns the constant stage size (max body +
    shoulders), so the Stage is arranged once; layers already have fixed final `Width/Height`, top-centered.
  * The clip is one cached, unfrozen `RectangleGeometry` whose `Rect`/`RadiusX`/`RadiusY` are mutated each frame (a composition
    update, not layout). Draw the body into a retained `DrawingGroup` that `OnRender` draws once; each frame calls
    `using var dc = _body.Open()`. This skips `InvalidateVisual`, which would also trigger an arrange.
  * Animate content only via `RenderTransform` (Scale/Translate), `Opacity` and `Effect.Radius`. Replace the tab indicator's
    `Width` animation with a render-only pill drawn from two edge channels.
  * The Rendering handler runs after layout, so touching an `AffectsMeasure` property there runs layout **a second time** that
    frame ([CompositionTarget docs](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/how-to-render-on-a-per-frame-interval-using-compositiontarget)).
* **Caching.** During enter/exit transitions, give the content layer `CacheMode = new BitmapCache(dpiScale)`: text, art and icons
  then scale and fade as a texture instead of being re-rasterized at each scale. Set it to null when settled. Don't cache
  subtrees that update every frame (equalizer, wavy progress); keep them outside the cached element. Set
  `TextOptions.TextHintingMode="Animated"` on layers that scale or move (stops hinting jitter), and
  `RenderOptions.BitmapScalingMode="Linear"` on album art while it animates (Fant/HighQuality are slower).
* **Blur (`BlurEffect`, GPU pixel shader).** Acceptable on small layers ≤ ~400×140 DIP (live activity, peek, HUD) with radius
  ≤ 8, for ≤ 220 ms, `RenderingBias="Performance"`. Put the effect on a wrapper and the `BitmapCache` on its child. **Set
  `Effect = null` once the radius is < 0.25**: a radius-0 effect still forces an intermediate surface. Never blur the 820×640
  expanded layer. Disable blur when `RenderCapability.Tier >> 16 < 2` (software rendering).
* **Shadow.** Replace `DropShadowEffect` with 4–6 concentric translucent strokes drawn in `OnRender` (the existing glow technique),
  faded in with `FxSlow` after the expand settles.
* **Allocations.** Nothing per frame: no `new Pen`/`Brush` (cache frozen pens per brush), no LINQ, closures or `Storyboard`s. One
  small `StreamGeometry` per frame is fine. Boxing a double in `SetValue` (24 B) is fine at 144 Hz × ~12 channels; use plain
  fields + `ISpringSink` for the hot shape.
* Keep `UseLayoutRounding=False` on the window for subpixel motion, and snap to device pixels at rest (§2).

## 4. "Refreshed" choreography

Token set (T s / ζ; in parentheses: k):

| name | spring | role |
|---|---|---|
| `Grow` | 0.40 / 0.80 (247) | panel height when it grows |
| `GrowWide` | 0.36 / 0.78 (305) | panel width when it grows (leads height → stretch) |
| `Shrink` | 0.36 / 0.92 (305) | height when it shrinks (firm, 0.15 % overshoot) |
| `ShrinkWide` | 0.40 / 0.90 (247) | width when it shrinks (trails height → retract-then-narrow) |
| `Small` | 0.30 / 0.75 (439) | HUD, live activity and peek width |
| `ContentIn` | 0.32 / 0.86 (386) | content scale and translate in |
| `Pop` | 0.28 / 0.62 (504) | small icons and art only |
| `Press` | 0.12 / 1.0 (2742) | press-down (no bounce) |
| `Release` | 0.26 / 0.62 (584) | press release (≈ M3 SpatialFast) |
| `Data` | 0.24 / 1.0 (685) | values that represent data (levels, slider jumps) — never overshoot |
| `FxFast` / `Fx` / `FxSlow` | 0.10, 0.16, 0.22 / 1.0 (≈ M3 Effects 3800/1600/800) | opacity, blur, color |

* Growth vs. shrink is chosen **per channel at retarget time** from sign(target − value), so a reversal mid-flight picks the
  right spring automatically.
* Content scale origin is **top-center** (content drops out of the bezel). Blur only on small layers (§3).
* **Shoulder bloom (gooey neck):** drawn `Rs = Rs channel value + clamp(|v_H| / 800, 0, 4)` DIP, where v_H is the height
  channel's velocity. At peak expand speed (~3000–3500 DIP/s) the fillets flare by 4 DIP, then relax as H settles, which reads
  as liquid pulled from the top edge. It stays smooth because v_H is continuous.

| transition | container (t = 0 at trigger) | content | notes |
|---|---|---|---|
| **Collapsed → Peek** (after HoverDelayMs) | W `Small`, H 0.38/0.80, Rb with H, Rs 0.30/0.9 + bloom | at 70 ms: α 0→1 `Fx`, scale 0.92→1 + y −6→0 `ContentIn`, blur 8→0 `FxSlow`; inner groups (art, text, controls) stagger 25 ms on α/y | glow α `FxSlow` |
| **Peek → Expanded** (click) | W `GrowWide`, H `Grow`, Rb 28→32, Rs 12→14 | peek out: α→0 `FxFast`, scale 1→1.04 (zoom-through, like pressing into the island), blur 0→6 `FxFast`. Expanded in: header at 80 ms (α `Fx`, y −8→0 `ContentIn`); tab body at 120 ms (α `Fx`, scale 0.96→1, y −12→0, **no blur**) | shadow α 0→1 `FxSlow` when H is within 2 % of target |
| **Collapsed/Live → Expanded** (hotkey, ExpandOnHover, click) | as above | header 100 ms, body 140 ms | press release velocity is kept (see Press) |
| **Expanded → Collapsed** | shadow α→0 `FxFast` at 0; H `Shrink` at 40 ms; W `ShrinkWide` at 70 ms; Rb with H; Rs 0.30/1.0 | content out first: α→0 `FxFast`, scale 1→0.97 and y 0→−8 with 0.24/1.0; live layer (if any) in at 180 ms: α `Fx`, scale 0.9→1 `Pop`, blur 6→0 | perceived ≈ 300 ms, settled ≈ 450 ms; no rubber-band |
| **Peek → Collapsed** | H 0.32/0.95 at 30 ms, W 0.34/0.92 at 50 ms | α→0 `FxFast`, blur 0→6 | |
| **HUD in** (volume/brightness/notification) | W `Small`, H 0.32/0.80 | at 50 ms: α `Fx`, blur 6→0 `Fx`; icon scale 0.6→1 `Pop`; level bar `Data`; notification title/body stagger 30 ms (y −6→0) | |
| **HUD update** (while shown) | — | bar retargets `Data` (velocity kept for key-repeat bursts); icon `AddVelocity(+0.8/s)` → ≈ 2 % nudge that springs back | no re-entry, no flash |
| **HUD out** (after 1.6 s idle) | H 0.34/0.95 at 40 ms, W 0.34/0.95 | α→0 `FxFast` | new event mid-out → retarget back, α reverses from current |
| **Tab switch** | panel H 0.38/0.85 at 0 | old: α→0 `FxFast`, x 0→−16·dir 0.26/1.0; new at 40 ms: α `Fx`, x +24·dir→0 0.34/0.88 | indicator edges: leading 0.22/0.80, trailing 0.32/0.80 → pill stretches in flight, then contracts; label color `Fx` |
| **Live activity appear** | W 200→300 0.40/0.75 (≈ 2.8 DIP overshoot), H 12→34 0.36/0.85, Rb 9→17 with H | art at 90 ms: scale 0.5→1 `Pop`, α `Fx`, blur 6→0 `Fx`; EQ bars at 120 ms, stagger 30 ms, scaleY 0→1 0.24/0.70 (origin bottom) | track change: old art blur 0→6 + α→0 `Fx`, new art blur 6→0 + α→1 |
| **Live activity disappear** | W 0.34/0.95 and H 0.30/1.0 at 60 ms | art/EQ α→0 + scale→0.8 `FxFast` | |
| **Notch hover** (collapsed/live) | W +6, H +2, 0.30/0.70 | — | "breathing"; 0.3 DIP overshoot, felt rather than seen |
| **Notch press** | scaleX 0.97 / scaleY 0.92 (origin top) `Press`; release `Release` | — | if the release expands, expansion starts at release and the scale returns concurrently |
| **Button press** (M3) | radius full → 8 `Fx` (no bounce, as M3); scale 0.96 `Press` | release: radius `Fx`, scale `Release`; state layer α 0→0.08 `FxFast` | button groups: pressed +15 % width, neighbors compress, 0.22/0.60 |
| **Toggle / QS tile** | radius 20 → pill 0.30/0.80 | color `Fx` | |
| **Slider grab / drag / release** | handle width 4→2 `Fx` on press (M3 halves it); value tracks the pointer **1:1, no spring** while dragging; release: handle 2→4 `Release` | click on track ≥ 8 DIP away: value springs 0.12/1.0 to the pointer, then tracks 1:1 | keyboard, wheel, external (system volume): `Data` with velocity kept; wavy progress follows the playback clock (no spring) |
| **List add** | slot height 0→h 0.32/0.95; neighbors FLIP (translateY old→new, 0.32/0.90) | item at 40 ms: α `Fx`, scale 0.96→1 `ContentIn` | batch stagger 25 ms, max 6 items |
| **List remove** | slot h→0 0.28/1.0 at 60 ms; neighbors FLIP | α→0 + scale→0.96 `FxFast` | swipe-dismiss: pass gesture velocity via `AddVelocity` |

**Interruptions (global rules):**
1. Retarget, never restart: x and v are kept, only the spring changes (Apple and Android do the same).
2. A new state replaces all pending delays: each channel either retargets now or gets a new delay.
3. Layer visibility is derived: a layer is Visible while α > 0 or target α > 0, and becomes Collapsed in α's `Settled`
   handler (the handler first checks the layer is still not the active one).
4. A layer re-selected mid-exit reverses from its current α, scale and y. There is no reset to 0, and the opacity never blinks.
5. Rapid hover in/out within `HoverDelayMs`/`CollapseDelayMs` is handled by the existing state timers. The springs only
   guarantee that whatever happens stays continuous.
6. At rest: geometry snaps to device pixels, α is exactly 0 or 1, `Effect = null`, `CacheMode = null`.
7. `ReduceMotion` keeps today's semantics: dt = 10 lands every channel in one frame. Screenshot mode forces it on.

## 5. Legacy and migration

**Legacy keeps current behavior exactly:** `SpringEase` on `DoubleAnimation` with a fixed settle duration and
`SnapshotAndReplace` handoff (velocity lost). One spring for W/H/Rb/Rs (`SpatialDefault`; `SpatialFast` for peek, HUD and
hover), hover breathing +4/+2. Content cross-fade: out 90 ms linear, in after 60 ms over 180 ms `CubicEase` from y −8 and scale
0.97. Tabs: 24 px slide, 90 ms out / 180 ms in; the indicator animates X and `Width` with `SpatialFast`. Squish 0.97/0.9 with
`EffectsFast`, release `SpatialFast`. `DropShadowEffect` while expanded.

Visually neutral perf fixes go to **both** profiles (render-only NotchShape geometry, cached pens, no per-frame clip
allocation), so the A/B compares motion design, not jank.

**API (app code only talks to this):**

```csharp
// NotchHub.Core.Settings
public enum AnimationStyle { Legacy, Refreshed }
public sealed class AppearanceSettings { /* … */ public AnimationStyle AnimationStyle { get; set; } = AnimationStyle.Refreshed; }

// NotchHub.UI.Animation
public enum MotionProfile { Legacy, Refreshed }
public enum MotionToken { SpatialFast, SpatialDefault, SpatialSlow, EffectsFast, EffectsDefault, EffectsSlow }
public static partial class Motion
{
    public static MotionProfile Profile { get; set; }          // set in App.xaml.cs next to ReduceMotion (lines ~76, ~199)
    public static event Action? ProfileChanged;
    // Legacy → SpringEase/DoubleAnimation (Springs.*); Refreshed → per-(object,DP) SpringChannel, velocity preserved.
    public static void Animate(this DependencyObject o, DependencyProperty dp, double to, MotionToken token, Action? completed = null);
    public static void SnapAll();                              // finish everything at targets (profile switch, screenshot)
}

// NotchHub.App.Shell — the choreography lives behind one interface, two implementations
public interface INotchMotion
{
    void MorphTo(ShellState state, NotchGeometry target, bool instant);   // W, H, Rb, Rs
    void SwitchLayer(FrameworkElement? from, FrameworkElement? to, ShellState state, bool instant);
    void SwitchTab(FrameworkElement? from, FrameworkElement to, int dir, bool instant);
    void MoveIndicator(double x, double width, bool instant);
    void Squish(bool down);
    void SetHover(bool hovering);
    void SetShadow(bool on);
}
// LegacyNotchMotion = today's ApplyGeometry/SwitchLayer/SelectTab/Squish code moved verbatim.
// RefreshedNotchMotion = SpringChannel-based, spec §4.
```

**Steps:**
1. Add `AnimationStyle` to `AppearanceSettings` and a segmented control in Settings → Appearance ("Animation style: Refreshed
   / Legacy", pl + en strings). Read it in `App` and set `Motion.Profile`.
2. Add `Spring`, `SpringChannel`, `SpringAnimator`, `DpSink`, `DisplayInfo.RefreshHz` and `MotionToken` to `NotchHub.UI.Animation`.
   Keep `SpringEase`/`Springs` for Legacy. Map each token to a Legacy `SpringEase` and a Refreshed `Spring` (M3 Expressive
   values via `Spring.FromM3`).
3. Mechanically replace `Springs.X` with `MotionToken.X` at the ~13 call sites in `NotchHub.UI.Controls` (Wavy, Misc, M3Slider,
   QsTile, Buttons) and in the shell. Then add the §4 specifics to the Refreshed branch for M3Slider (handle halving, 1:1 drag),
   Buttons (radius → 8 with `Fx`) and the indicator.
4. Refactor `NotchShape` (render-only, retained `DrawingGroup`, mutable clip, shoulder-bloom hook, stroke shadow). Extract
   `INotchMotion` from `NotchWindow.xaml.cs` / `NotchWindow.Tabs.cs`. On `ProfileChanged`, call `Motion.SnapAll()` and swap the
   implementation; it takes effect immediately, no restart.
5. **Unit tests:** the analytic step is dt-independent (0.5 s stepped as 30 × 1/60 s or 72 × 1/144 s gives the same state
   within 1e-9); velocity is identical before and after a retarget; settle happens within the expected time;
   `SpringAnimator.IsRunning == false` after settle (zero idle CPU). **Manual:** debug overlay (fps, dropped frames),
   `NOTCHHUB_MOTION_SCALE=0.2` slow-mo, 120/144 fps screen recordings of Legacy vs Refreshed for every row of §4, and 0 % CPU
   at idle in both profiles.
6. Ship Refreshed as the default once the §4 table is signed off. Legacy stays selectable.
