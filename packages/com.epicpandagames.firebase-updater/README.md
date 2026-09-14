# Firebase Installer / Updater

An editor-only tool for updating an Assets-based Firebase Unity SDK installation from an official Firebase Unity SDK ZIP.

Open it from `Tools > Epic Panda Games > Firebase Updater`.

## Features

- Detects the currently installed Firebase version and modules from Firebase manifests.
- Inspects SDK ZIP and `.unitypackage` contents before changing the project.
- Trusts package manifests instead of the archive filename when detecting versions.
- Preselects currently installed modules while allowing the selection to be changed.
- Shows exact additions, replacements, removals, preserved files, and filtered files.
- Preserves Firebase application configuration files.
- Filters the Firebase-bundled Assets copy of External Dependency Manager so EDM4U can remain UPM-managed.
- Stages and validates package contents before a confirmed clean update.
- Detects unsafe archive paths and conflicting files across selected modules.
- Queues Android dependency resolution and reports interrupted operations.
- Encodes spaces in the generated Gradle local-repository URI for affected Gradle 9 projects.

## Install

Add the package to the consuming project's `Packages/manifest.json` using a release tag:

```json
"com.epicpandagames.firebase-updater": "https://github.com/EpicPandaGames/unity-tools.git?path=/packages/com.epicpandagames.firebase-updater#firebase-updater-v0.1.1"
```

The repository is private. Configure Git authentication on the developer machine or CI runner; do not put credentials in the manifest.

## Safety model

Archive inspection and preview are read-only with respect to the Unity project. A clean update occurs only after staging succeeds and the user confirms the displayed change summary. The package intentionally does not retain a rollback backup after a confirmed update.

Copyright Epic Panda Games. Internal use only.
