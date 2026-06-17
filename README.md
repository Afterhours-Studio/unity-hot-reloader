# Unity Reloader

Hot reload for Unity. Iterate on code without breaking your play session.

1. Enter Play Mode
2. Edit a script
3. See changes applied instantly - no recompile, no domain reload

## Requirements

- Unity 2022.3+
- Windows (Direct Windows API watcher) or any platform (polling mode)

## Installation

Add via Unity Package Manager using the git URL:

```
git@github.com:Afterhours-Studio/unity-hot-reloader.git?path=Assets
```

Or clone and add as a local package.

## How it works

Unity Reloader watches your script files for changes, recompiles only the modified file using Roslyn, then uses Harmony to detour method bodies at runtime - no domain reload needed.

**Supported changes (hot reload):**
- Method body edits

**Unsupported changes (triggers full recompile automatically):**
- Adding / removing fields
- Changing field types
- Toggling `[SerializeField]`
- Adding new types

## Dashboard

Open via `Tools > Unity Reloader > Dashboard`.

Configure file watchers, exclusions, and preferences from the dashboard. The Activity tab shows a live log of every hot reload event.

## License

MIT
