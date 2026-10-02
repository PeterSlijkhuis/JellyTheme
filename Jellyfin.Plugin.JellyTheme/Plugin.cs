using System;
using MediaBrowser.Common.Plugins;

namespace Jellyfin.Plugin.JellyTheme;

/// <summary>
/// JellyTheme: theme songs for every movie and show.
/// </summary>
public class Plugin : BasePlugin
{
    /// <inheritdoc />
    public override string Name => "JellyTheme";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("a0373070-26ce-4f00-92ce-d760f5edc5cb");

    /// <inheritdoc />
    public override string Description => "Downloads theme songs for your movies and TV shows.";
}
