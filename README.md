# JellyTheme

Jellyfin plugin that adds theme songs to your movies and TV shows.

## What it does today

The scheduled task **Download TV theme songs from Plex** (Dashboard > Scheduled Tasks > JellyTheme, runs daily) saves `theme.mp3` into every TV show folder that has a TVDB id and no theme yet. Themes come from Plex's public theme server (`https://tvthemes.plexapp.com/<tvdb id>.mp3`). Shows that already have `theme.mp3` or a `theme-music` folder are left alone. Jellyfin picks up new themes on the next library scan.

## Build and install

Requires the .NET 10 SDK and Jellyfin 12.1+.

```sh
dotnet build -c Release
dotnet test
```

Copy `Jellyfin.Plugin.JellyTheme/bin/Release/net10.0/Jellyfin.Plugin.JellyTheme.dll` into a `JellyTheme` folder under your Jellyfin `plugins` directory and restart Jellyfin.
