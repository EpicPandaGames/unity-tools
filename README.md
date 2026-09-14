# Epic Panda Games Unity Tools

Private Unity Package Manager packages shared by Epic Panda Games projects.

## Packages

| Package | Description | Current version |
| --- | --- | --- |
| `com.epicpandagames.firebase-updater` | Inspect, validate, and cleanly update the Firebase Unity SDK from an official SDK ZIP. | `0.1.0` |
| `com.epicpandagames.bulk-rename-files` | Sequentially rename files in a project folder. | `1.0.0` |
| `com.epicpandagames.bulk-rename-folders` | Sequentially rename child folders and optionally their textures. | `1.0.0` |
| `com.epicpandagames.rename-selected-gameobjects` | Rename selected scene GameObjects with Undo support. | `1.0.0` |
| `com.epicpandagames.replace-with-prefab` | Replace selected scene objects with a prefab while retaining transforms. | `1.0.0` |
| `com.epicpandagames.resize-texture` | Resize a texture onto a transparent 1024-square canvas. | `1.0.0` |
| `com.epicpandagames.resize-pow2` | Pad a texture to a square power-of-two canvas. | `1.0.0` |

Packages are versioned independently. Projects should reference release tags rather than `main`.

## Authentication

This is a private repository. Developer machines must authenticate Git through Git Credential Manager or SSH. Never embed credentials in a Unity `Packages/manifest.json` file.

Copyright Epic Panda Games. Internal use only.
