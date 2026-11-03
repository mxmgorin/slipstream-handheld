# slipstream-handheld

Full-screen races for [Slipstream](https://store.steampowered.com/app/732810/) by [ansdor](https://www.ansdor.com/) on screens taller than 16:9, such as 4:3 and 1:1. Menus stay 16:9. Used by the game's [PortMaster](https://portmaster.games/) port.

## Requirements

| | |
|--|--|
| Game | Slipstream v1.3.3-R6 for Linux, Steam build 15704123 |
| Build | .NET 8 SDK, Python 3 |
| Run | .NET 8 runtime |

## Build

`GAME` is the game's Steam folder.

```bash
mkdir game
python3 tools/unbundle.py GAME/slipstream slipstream.dll game/slipstream.dll
cp GAME/FNA.dll game/
GAME_DIR=game ./build.sh
```

`DOTNET=path/to/dotnet` selects an SDK other than the one on `PATH`. Output in `out/`:

| File | Role |
|--|--|
| `aspect-patch.dll`, `aspect-patch.runtimeconfig.json` | Patcher |
| `Mono.Cecil.dll`, `Mono.Cecil.Rocks.dll` | Patcher libraries |
| `SlipstreamAspect.dll` | Runtime helper |

## Usage

```bash
dotnet aspect-patch.dll GAME_DIR/slipstream.dll
cp SlipstreamAspect.dll GAME_DIR/
```

| Exit status | Meaning |
|--|--|
| 0 | Patched, or already patched |
| 1 | Unsupported game build or unreadable file; nothing written |

On ARM64, `slipstream.dll` also needs its PE machine set to ARM64; the PortMaster port does this.

## Settings

Environment variables read by the patched game:

| Variable | Effect |
|--|--|
| `SLIPSTREAM_HORIZON=0`…`100` | Horizon and car height in races: higher is lower, 0 centres the horizon; default 60 |
| `SLIPSTREAM_ASPECT=16:9` | Original 16:9 picture with black bars |

## License

MIT, see [LICENSE](LICENSE). Uses [Mono.Cecil](https://github.com/jbevain/cecil) (MIT).
