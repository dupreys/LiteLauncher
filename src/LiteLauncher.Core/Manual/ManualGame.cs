namespace LiteLauncher.Core.Manual;

/// <summary>A game the user added by choosing its .exe (outside Steam and Epic).</summary>
public sealed class ManualGame
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string ExePath { get; set; } = "";
}
