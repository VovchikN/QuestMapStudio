# Quest Map Studio

Quest Map Studio is a Windows x64 desktop app for managing custom maps and playlists in modded Beat Saber 1.40.8 on Meta Quest.

## Features

- Browse, search, and sort your maps with cover art, difficulty labels, and mod requirements.
- Install maps from ZIP archives and remove selected maps from your headset.
- Create, edit, import, and export playlists. Reorder tracks and playlists before applying changes.
- Download missing tracks from BeatSaver when importing a playlist. Maps already on the headset are skipped.
- Use local drafts and undo or redo changes before applying them to your headset.

## Getting started

1. Download the Windows x64 ZIP from [Releases](../../releases), extract it, and run `QuestMapStudio.exe`. The package includes .NET and ADB.
2. Enable Developer Mode on your Quest, connect it via USB, and accept the USB debugging prompt.
3. Choose **Find Quest**, select your headset if prompted, and click **Connect**.
4. Close Beat Saber before clicking **Apply**. Reopen the game to see your changes.

The headset needs a compatible modded Beat Saber installation with SongCore and playlist support.

## Build from source

Install the .NET 10 SDK and run these commands from the repository root.

```sh
dotnet run --project Tests -c Release
dotnet run --project UiTests -c Release
dotnet publish App/QuestMapStudio.csproj -c Release -r win-x64 --self-contained true -o publish
```

For a locally built Windows app, put Windows Platform Tools in a `platform-tools` folder next to the executable.

Source code is available under the [MIT License](LICENSE). See [third-party notices](THIRD-PARTY-NOTICES.md) for bundled components.
