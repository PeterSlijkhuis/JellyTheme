using System;
using System.Collections.Generic;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.JellyTheme;

/// <summary>
/// JellyTheme: theme songs for every movie and show.
/// </summary>
public class Plugin : BasePlugin, IHasWebPages
{
    /// <inheritdoc />
    public override string Name => "JellyTheme";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("a0373070-26ce-4f00-92ce-d760f5edc5cb");

    /// <inheritdoc />
    public override string Description => "Downloads theme songs for your movies and TV shows.";

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
        => [new PluginPageInfo
        {
            Name = Name,
            DisplayName = "JellyTheme",
            EmbeddedResourcePath = GetType().Namespace + ".Web.themepicker.html",
            EnableInMainMenu = true,
            MenuIcon = "music_note",
        }];
}
