# JellyTheme

A Jellyfin plugin that adds theme songs to your movies and TV shows, with as little work from you as possible.

It fills themes automatically from two trusted sources, and gives you a quick picker page for whatever is left.

## What it does

1. **Plex TV themes (automatic).** For TV shows with a TVDB id, it downloads the theme from Plex's public theme server and saves it as `theme.mp3` in the show folder.
2. **ThemerrDB (automatic).** For movies and TV shows with a TMDB id, it looks up the YouTube theme that the ThemerrDB community picked and saves its audio as `theme.m4a`. The audio is not re-encoded.
3. **Missing themes page (manual).** For everything still without a theme, a page in the dashboard lists those items. Per item you search YouTube, listen to the top 5 results right in the browser, and press "Use this" on the right one.

Automatic sources run:

- **Daily**, as the scheduled task **Download theme songs** (Dashboard > Scheduled Tasks > JellyTheme). You can also start it by hand there.
- **For new items**, as soon as Jellyfin fetches their metadata. You don't have to wait for the daily run.

After a theme is saved, the plugin asks Jellyfin to refresh that item, so the theme plays without a full library scan.

## What it does not do

- **It never replaces a theme you already have.** Any `theme.*` file or `theme-music` folder means the item is skipped. To replace a theme, delete the file first.
- **No themes for movies that share a folder.** Jellyfin plays a folder's theme for every movie in it, so a theme there would play for all of them. Put each movie in its own folder to get one.
- **No themes for episodes, seasons, music or other library types.** Only movies and shows.
- **No automatic YouTube guessing.** YouTube search only happens on the Missing themes page, and only you decide what gets saved. That keeps wrong songs out of your library.
- **No settings to configure yet.** It works out of the box.
- **No plugin catalog yet.** You install it by hand (see below).

## Requirements

- Jellyfin **12.1 or newer**. The plugin is built against Jellyfin 12.1 and .NET 10, so it will not load on 10.x servers.
- The Jellyfin server needs **write access** to your movie and show folders.
- The server needs internet access to `tvthemes.plexapp.com`, `app.lizardbyte.dev` and `youtube.com`.

## Install

1. **Build the plugin** (needs the [.NET 10 SDK](https://dotnet.microsoft.com/download)):

   ```sh
   git clone https://github.com/PeterSlijkhuis/JellyTheme.git
   cd JellyTheme
   dotnet build -c Release
   ```

2. **Find your Jellyfin plugins folder.** It sits inside Jellyfin's data folder. Common locations:

   | Setup | Plugins folder |
   |---|---|
   | Linux (package) | `/var/lib/jellyfin/plugins` |
   | Docker (official image) | `/config/plugins` inside the container |
   | Docker (linuxserver.io image) | `/config/data/plugins` inside the container |
   | Windows | `%ProgramData%\Jellyfin\Server\plugins` |
   | macOS | `~/.local/share/jellyfin/plugins` |

   If none of these match, open Dashboard in Jellyfin and look for the data path under "Paths". The `plugins` folder is inside it.

3. **Copy the files.** Create a folder named `JellyTheme` in the plugins folder. Copy both of these files into it from `Jellyfin.Plugin.JellyTheme/bin/Release/net10.0/`:
   - `Jellyfin.Plugin.JellyTheme.dll`
   - `YoutubeExplode.dll`

   Both are required. Without `YoutubeExplode.dll` the plugin fails to load.

4. **Restart Jellyfin.**

5. **Check it loaded.** Go to Dashboard > Plugins. JellyTheme should be listed as Active. If it shows an error, check Dashboard > Logs for lines mentioning JellyTheme.

## First run

1. Go to Dashboard > Scheduled Tasks > JellyTheme > **Download theme songs** and press play. On a large library the first run takes a while, since it checks every item once.
2. Open the **JellyTheme** page from the dashboard menu to see what is still missing, and fill those in by hand.

After that, there is nothing you have to do. New movies and shows get themes on their own, and the daily task picks up anything that failed earlier.

## Using the Missing themes page

1. Dashboard menu > **JellyTheme**. Only administrators can open it.
2. Narrow the list with the name filter or the Movies/Shows selector. The page shows 100 items at a time.
3. Press **Find themes** on an item. The search box is pre-filled (title, year, and "main theme" or "opening theme"). Edit it if the results are wrong, for example by adding "soundtrack" or the composer's name.
4. Press play on a result to listen. Each preview streams through your Jellyfin server.
5. Press **Use this** on the right one. The item disappears from the list, and its theme plays from then on.

## Be careful with

- **YouTube's terms.** Downloading audio from YouTube may be against YouTube's Terms of Service, and theme songs are copyrighted. Use this only for your own personal library, and decide for yourself whether you're comfortable with it. Plex themes don't involve YouTube.
- **YouTube changes break downloads.** YouTube regularly changes how its site works. When that happens, ThemerrDB downloads and the Missing themes page stop working until the plugin is rebuilt with a newer YoutubeExplode. Plex themes keep working. Watch the Jellyfin log for "Could not save theme" warnings.
- **Read-only media folders.** If your media is mounted read-only (for example a Docker volume with `:ro`), or the Jellyfin user can't write to it, saving fails and the log shows a permission error. The plugin never changes anything except adding `theme.mp3` or `theme.m4a`.
- **The Plex theme server is unofficial.** Plex doesn't document it for outside use, so it could change or disappear without notice.
- **Your access token appears in preview URLs.** A browser `<audio>` element can't send login headers, so the preview link includes your Jellyfin access token. This is the same way Jellyfin's own web player streams media, but it means the token can show up in reverse-proxy logs.
- **Wrong picks are on you.** ThemerrDB entries are checked by its community, but nobody checks what you pick on the Missing themes page. If you pick the wrong song, delete the `theme.m4a` file in that item's folder and the item shows up in the list again.

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| Plugin not listed after restart | Files are not in their own `JellyTheme` folder, or `YoutubeExplode.dll` is missing |
| Plugin shows "Not supported" | Jellyfin is older than 12.1 |
| A show gets no theme | No TVDB or TMDB id (identify it in Jellyfin), or neither Plex nor ThemerrDB has a theme. Use the Missing themes page. |
| A movie gets no theme | It shares a folder with other movies, has no TMDB id, or ThemerrDB has no entry |
| "Search failed" or previews won't play | YouTube changed something, or the server can't reach YouTube. Check the Jellyfin log. |
| Theme saved but doesn't play | Wait a minute for the refresh, or refresh the item's metadata by hand. Also check that theme music is turned on in your user settings (Display > Theme songs). |

## Credits

JellyTheme combines ideas from two existing plugins and builds on open data and libraries. No code was copied from the plugins below; JellyTheme is a separate implementation.

- **[Themerr-jellyfin](https://github.com/LizardByte/Themerr-jellyfin)** by LizardByte (AGPL-3.0). The idea of filling themes automatically from ThemerrDB comes from Themerr.
- **[ThemerrDB](https://github.com/LizardByte/ThemerrDB)** by LizardByte and its contributors. The community database of YouTube theme links that JellyTheme looks themes up in.
- **[xThemeSong (Jellyfin.Plugin.AssignThemeSong)](https://github.com/kirtan3d/Jellyfin.Plugin.AssignThemeSong)** by kirtan3d. The idea of choosing a YouTube theme by hand per item comes from xThemeSong.
- **Plex** for the public TV theme server at `tvthemes.plexapp.com`. JellyTheme is not affiliated with or endorsed by Plex.
- **[YoutubeExplode](https://github.com/Tyrrrz/YoutubeExplode)** by Tyrrrz (MIT). Used for YouTube search and audio download. It ships with the plugin as `YoutubeExplode.dll`.
- **[Jellyfin](https://jellyfin.org)** for the server and plugin API.

### What is new in JellyTheme

- Plex, ThemerrDB and manual picking in one plugin, tried in that order, so each item gets the most reliable theme available.
- Themes for new items as soon as their metadata arrives, plus an immediate refresh so they play right away.
- The Missing themes page: a filtered list of everything without a theme, with several YouTube results to listen to side by side before saving.
- Safe file handling: downloads go to a temporary file first, so a dropped connection never leaves a broken theme. Existing themes are never touched.

## Development

```sh
dotnet build
dotnet test
```

The tests use fake HTTP responses and never contact Plex, ThemerrDB or YouTube.
