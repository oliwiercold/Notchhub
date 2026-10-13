using System.Windows;
using NotchHub.Core;
using NotchHub.Core.Contracts;

namespace NotchHub.Features.Compact;

/// <summary>Extra states for tools/preview.</summary>
internal static class PreviewStates
{
    public static IEnumerable<string> Names(FrameworkElement view) => view switch
    {
        PeekView => ["glance", "glance-media"],
        LiveActivityView => ["meeting-bar", "meeting-ongoing", "meeting-media"],
        HudView => ["call-ringing", "call-talking", "charging", "unplugged", "otp"],
        _ => [],
    };

    public static Task ApplyAsync(FrameworkElement view, string state)
    {
        var now = DateTimeOffset.Now;
        switch (view, state)
        {
            case (PeekView pv, "glance"):
                pv.ViewModel.ShowsMedia = false;
                break;
            case (PeekView pv, "glance-media"):
                AppServices.Settings.Update(s => s.Notch.PeekGlanceWithMedia = true);
                pv.ViewModel.ShowsMedia = true;
                pv.ViewModel.IsCombined = true;
                break;
            case (LiveActivityView lv, "meeting-bar"):
                lv.ViewModel.SetPreview(Event("Korki – Karolina, matematyka rozszerzona", now.AddMinutes(22.5), 60), media: false);
                break;
            case (LiveActivityView lv, "meeting-ongoing"):
                lv.ViewModel.SetPreview(Event("Spotkanie zespołu", now.AddMinutes(-18), 30), media: false);
                break;
            case (LiveActivityView lv, "meeting-media"):
                lv.ViewModel.SetPreview(Event("Korki – Karolina", now.AddMinutes(9.5), 60), media: true);
                break;
            case (HudView hv, "call-ringing"):
                hv.ViewModel.Present(new HudRequest(HudKind.PhoneCall, new PhoneCall(PhoneCallState.Ringing, "Mama", "+48 600 123 456", now, null)));
                break;
            case (HudView hv, "call-talking"):
                hv.ViewModel.OnShown();
                hv.ViewModel.Present(new HudRequest(HudKind.PhoneCall, new PhoneCall(PhoneCallState.Talking, "Mama", "+48 600 123 456", now.AddSeconds(-151), null)));
                break;
            case (HudView hv, "charging"):
                hv.ViewModel.Present(new HudRequest(HudKind.PhoneCharging, new PhoneBattery(76, true, false)));
                break;
            case (HudView hv, "unplugged"):
                hv.ViewModel.Present(new HudRequest(HudKind.PhoneCharging, new PhoneBattery(12, false, true)));
                break;
            case (HudView hv, "otp"):
                hv.ViewModel.Present(new HudRequest(HudKind.PhoneNotification, new PhoneNotification(
                    "otp", "Wiadomości", "mBank", "Kod BLIK 482 913 do transakcji w sklepie. Nie podawaj go nikomu.", null,
                    now.AddMinutes(-1), true, false, null, [], null)));
                break;
        }
        return Task.CompletedTask;
    }

    private static CalendarEvent Event(string title, DateTimeOffset start, int minutes) =>
        new("preview", title, start, start.AddMinutes(minutes), false, null, null, "local", "#E8710A");
}
