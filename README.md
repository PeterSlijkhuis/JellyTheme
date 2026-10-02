# JellyTheme

Jellyfin plugin that adds theme songs to your movies and TV shows.

## What it does today

The scheduled task **Download theme songs** (Dashboard > Scheduled Tasks > JellyTheme, runs daily) saves a theme into every movie and TV show folder that has none yet. It tries, in order:

1. **Plex** (TV shows, by TVDB id): `https://tvthemes.plexapp.com/<tvdb id>.mp3`, saved as `theme.mp3`.
2. **ThemerrDB** (movies and TV shows, by TMDB id): the community-picked YouTube theme from [ThemerrDB](https://github.com/LizardByte/ThemerrDB). Its audio track is saved as `theme.m4a`, no re-encoding.

Folders that already have a `theme.*` file or a `theme-music` folder are left alone. Movies only get a theme when they sit in their own folder, because a theme in a shared folder would play for every movie in it.

New items don't wait for the daily run: as soon as Jellyfin fetches their metadata, JellyTheme grabs a theme. Each saved theme triggers a refresh of that item, so it plays right away without a library scan.

YouTube downloads depend on YoutubeExplode. When YouTube changes something, ThemerrDB downloads fail until the plugin is updated with a newer YoutubeExplode; Plex themes keep working.

## Build and install

Requires the .NET 10 SDK and Jellyfin 12.1+.

```sh
dotnet build -c Release
dotnet test
```

Copy `Jellyfin.Plugin.JellyTheme.dll` and `YoutubeExplode.dll` from `Jellyfin.Plugin.JellyTheme/bin/Release/net10.0/` into a `JellyTheme` folder under your Jellyfin `plugins` directory and restart Jellyfin.
