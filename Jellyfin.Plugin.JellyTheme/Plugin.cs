using System;
using System.Collections.Generic;
using Jellyfin.Plugin.JellyTheme.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.JellyTheme;

/// <summary>
/// JellyTheme: theme songs for every movie and show.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    // The generic base sets the plugin's file path and version. The non-generic BasePlugin leaves them null,
    // which crashes Jellyfin's plugin list (GET /Plugins).

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Application paths.</param>
    /// <param name="xmlSerializer">Xml serializer.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
    }

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
