# More Dispatch Mod

A Cities: Skylines 2 mod that adds options to control additional emergency vehicle dispatch to car accidents and medical calls.

## Features

Three toggles in the Options dialog (all default OFF = vanilla behavior):

- **Police to All Accidents**: Force police dispatch to ALL car accidents, even low-severity ones that vanilla would skip.
- **Fire Engines to Accidents**: Additionally dispatch fire engines to all car accidents.
- **Fire Engines to Medical Calls**: Additionally dispatch fire engines to all medical calls requiring transport.

## Installation

1. Build the mod or download a release.
2. Place the `MoreDispatchMod.dll` in your Cities: Skylines II mods folder.
3. Enable the mod in the game's content manager.

## Building

1. Copy `Directory.Build.props.example` to `Directory.Build.props` and set your game installation path.
2. Run `dotnet build` from `src/MoreDispatchMod/`.

## License

MIT License - see [LICENSE](LICENSE) for details.
