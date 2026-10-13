namespace NotchHub.UI;

/// <summary>Implemented by tab / compact views. The shell calls these when the view becomes visible / hidden,
/// so view models can start/stop live updates (e.g. stats polling, media progress animation).</summary>
public interface ITabView
{
    void OnShown();
    void OnHidden();
}
