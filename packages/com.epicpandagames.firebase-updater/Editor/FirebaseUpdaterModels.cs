#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace EpicPandaGames.UnityTools.FirebaseUpdater
{
    [Serializable]
    public sealed class FirebaseModuleInfo
    {
        public string Name;
        public string PackagePath;
        public string Version;
        public bool Installed;
        public bool Selected;
        public readonly List<string> AssetPaths = new List<string>();
    }

    [Serializable]
    public sealed class FirebaseInstalledState
    {
        public string Version = "Not installed";
        public readonly List<string> Modules = new List<string>();
        public readonly HashSet<string> ManifestOwnedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> Warnings = new List<string>();
    }

    [Serializable]
    public sealed class FirebaseArchiveInspection
    {
        public string ZipPath;
        public string Version = "Unknown";
        public readonly List<FirebaseModuleInfo> Modules = new List<FirebaseModuleInfo>();
        public readonly List<string> Warnings = new List<string>();
        public readonly List<string> Errors = new List<string>();
        public bool IsValid { get { return Errors.Count == 0 && Modules.Count > 0; } }
    }

    public enum FirebaseFileChangeKind
    {
        Add,
        Replace,
        Remove,
        Preserve,
        Filter
    }

    [Serializable]
    public sealed class FirebaseFileChange
    {
        public FirebaseFileChangeKind Kind;
        public string Path;
        public string Reason;
    }

    [Serializable]
    public sealed class FirebaseUpdatePreview
    {
        public readonly List<FirebaseFileChange> Changes = new List<FirebaseFileChange>();
        public readonly List<string> Warnings = new List<string>();
        public readonly List<string> Errors = new List<string>();
        public bool IsValid { get { return Errors.Count == 0; } }
    }

    [Serializable]
    public sealed class FirebaseInstallationResult
    {
        public bool Success;
        public string InstalledVersion;
        public readonly List<string> InstalledModules = new List<string>();
        public readonly List<string> Messages = new List<string>();
        public readonly List<string> Errors = new List<string>();
    }
}
#endif
