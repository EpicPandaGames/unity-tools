#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace EpicPandaGames.UnityTools.FirebaseUpdater
{
    [InitializeOnLoad]
    public static class FirebaseUpdaterService
    {
        private const string WorkRoot = "Library/FirebaseUpdater";
        private const string PendingResolverKey = "EpicPandaGames.FirebaseUpdater.PendingResolver";
        private const string PendingInstallKey = "EpicPandaGames.FirebaseUpdater.PendingInstall";
        private const string ResolverStatusKey = "EpicPandaGames.FirebaseUpdater.ResolverStatus";
        private static readonly Regex ManifestRegex = new Regex(
            @"Firebase(?<module>[A-Za-z0-9]+)_version-(?<version>[0-9.]+)_manifest\.txt$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly string[] PreservedFileNames =
        {
            "google-services.json",
            "GoogleService-Info.plist"
        };

        static FirebaseUpdaterService()
        {
            if (EditorPrefs.GetBool(PendingResolverKey, false))
                EditorApplication.delayCall += CompletePendingResolution;
        }

        public static FirebaseInstalledState DetectInstalledFirebase()
        {
            FirebaseInstalledState state = new FirebaseInstalledState();
            string[] manifests = Directory.Exists("Assets/Firebase/Editor")
                ? Directory.GetFiles("Assets/Firebase/Editor", "Firebase*_version-*_manifest.txt", SearchOption.TopDirectoryOnly)
                : new string[0];

            HashSet<string> versions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string manifest in manifests)
            {
                Match match = ManifestRegex.Match(Path.GetFileName(manifest));
                if (!match.Success)
                    continue;
                string module = NormalizeModuleName(match.Groups["module"].Value);
                if (!state.Modules.Contains(module))
                    state.Modules.Add(module);
                versions.Add(match.Groups["version"].Value);
                foreach (string line in File.ReadAllLines(manifest))
                {
                    string path = line.Trim().Replace('\\', '/');
                    if (IsSafeAssetPath(path))
                        state.ManifestOwnedPaths.Add(path);
                }
                state.ManifestOwnedPaths.Add(manifest.Replace('\\', '/'));
            }

            state.Modules.Sort(StringComparer.OrdinalIgnoreCase);
            state.Version = versions.Count == 1 ? versions.First() : versions.Count == 0 ? "Not installed" : "Mixed: " + string.Join(", ", versions.ToArray());
            if (versions.Count > 1)
                state.Warnings.Add("The current Firebase installation contains mixed SDK versions.");
            return state;
        }

        public static FirebaseArchiveInspection InspectArchive(string zipPath)
        {
            FirebaseArchiveInspection inspection = new FirebaseArchiveInspection { ZipPath = zipPath };
            try
            {
                if (string.IsNullOrEmpty(zipPath) || !File.Exists(zipPath))
                    throw new FileNotFoundException("Firebase SDK ZIP was not found.", zipPath);
                string inspectionRoot = Path.Combine(WorkRoot, "Inspection-" + StablePathHash(zipPath));
                RecreateDirectory(inspectionRoot);

                using (FileStream archiveStream = File.OpenRead(zipPath))
                using (ZipArchive zip = new ZipArchive(archiveStream, ZipArchiveMode.Read))
                {
                    HashSet<string> packageNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (ZipArchiveEntry zipEntry in zip.Entries)
                    {
                        if (!zipEntry.Name.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (!packageNames.Add(zipEntry.Name))
                            throw new InvalidDataException("The archive contains duplicate package name " + zipEntry.Name);
                        string packagePath = Path.Combine(inspectionRoot, zipEntry.Name);
                        using (Stream input = zipEntry.Open())
                        using (FileStream output = File.Create(packagePath))
                            input.CopyTo(output);

                        FirebaseModuleInfo module = InspectPackage(packagePath, zipEntry.Name);
                        inspection.Modules.Add(module);
                    }
                }

                inspection.Modules.Sort(delegate(FirebaseModuleInfo a, FirebaseModuleInfo b)
                {
                    return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                });
                if (inspection.Modules.Count == 0)
                    inspection.Errors.Add("The ZIP does not contain Firebase .unitypackage files.");

                HashSet<string> versions = new HashSet<string>(inspection.Modules.Select(m => m.Version).Where(v => !string.IsNullOrEmpty(v)), StringComparer.OrdinalIgnoreCase);
                if (versions.Count != 1)
                    inspection.Errors.Add("Every selected Firebase package must report one consistent SDK version. Found: " + string.Join(", ", versions.ToArray()));
                else
                    inspection.Version = versions.First();

                string fileVersion = ExtractVersionFromText(Path.GetFileName(zipPath));
                if (!string.IsNullOrEmpty(fileVersion) && fileVersion != inspection.Version)
                    inspection.Warnings.Add("ZIP filename suggests " + fileVersion + ", but package contents report " + inspection.Version + ". Package contents are authoritative.");

                FirebaseInstalledState installed = DetectInstalledFirebase();
                foreach (FirebaseModuleInfo module in inspection.Modules)
                {
                    module.Installed = installed.Modules.Contains(module.Name);
                    module.Selected = module.Installed;
                }
            }
            catch (Exception exception)
            {
                inspection.Errors.Add(exception.Message);
            }
            return inspection;
        }

        public static FirebaseUpdatePreview BuildUpdatePreview(FirebaseArchiveInspection archive, FirebaseInstalledState installed)
        {
            FirebaseUpdatePreview preview = new FirebaseUpdatePreview();
            if (archive == null || !archive.IsValid)
            {
                preview.Errors.Add("Select a valid Firebase SDK ZIP first.");
                return preview;
            }

            List<FirebaseModuleInfo> selected = archive.Modules.Where(m => m.Selected).ToList();
            if (selected.Count == 0)
                preview.Errors.Add("Select at least one Firebase module.");
            HashSet<string> selectedVersions = new HashSet<string>(selected.Select(m => m.Version), StringComparer.OrdinalIgnoreCase);
            if (selectedVersions.Count != 1)
                preview.Errors.Add("Selected packages contain mixed Firebase versions.");

            HashSet<string> incoming = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (FirebaseModuleInfo module in selected)
            {
                foreach (string path in module.AssetPaths)
                {
                    if (ShouldFilter(path))
                    {
                        AddChange(preview, FirebaseFileChangeKind.Filter, path, "UPM manages External Dependency Manager");
                        continue;
                    }
                    if (ShouldPreserve(path))
                    {
                        AddChange(preview, FirebaseFileChangeKind.Preserve, path, "Project Firebase configuration is preserved");
                        continue;
                    }
                    incoming.Add(path);
                }
            }

            if (!incoming.Contains("Assets/Firebase/Plugins/Firebase.App.dll"))
                preview.Errors.Add("Selected packages do not include the required Firebase App core.");

            foreach (string oldPath in installed.ManifestOwnedPaths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                if (ShouldPreserve(oldPath) || ShouldFilter(oldPath))
                    continue;
                if (!incoming.Contains(oldPath))
                    AddChange(preview, FirebaseFileChangeKind.Remove, oldPath, "Owned by the previous Firebase SDK manifest");
            }
            foreach (string path in incoming.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                FirebaseFileChangeKind kind = File.Exists(path) || Directory.Exists(path)
                    ? FirebaseFileChangeKind.Replace
                    : FirebaseFileChangeKind.Add;
                AddChange(preview, kind, path, "Firebase " + archive.Version);
            }
            AddChange(preview, FirebaseFileChangeKind.Remove, "Assets/GeneratedLocalRepo/Firebase", "Regenerated by Android dependency resolution");
            preview.Warnings.AddRange(archive.Warnings);
            preview.Warnings.Add("Clean update has no retained rollback backup. Review the removal list before confirming.");
            return preview;
        }

        public static FirebaseUpdatePreview ValidateStagedUpdate(FirebaseArchiveInspection archive, FirebaseUpdatePreview preview, out string stageRoot)
        {
            stageRoot = Path.Combine(WorkRoot, "Stage-" + Guid.NewGuid().ToString("N"));
            if (preview == null || !preview.IsValid)
                return preview;
            try
            {
                RecreateDirectory(stageRoot);
                Dictionary<string, string> fingerprints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (FirebaseModuleInfo module in archive.Modules.Where(m => m.Selected))
                    UnityPackageReader.ExtractAssets(module.PackagePath, stageRoot, p => !ShouldFilter(p) && !ShouldPreserve(p), fingerprints);

                string core = Path.Combine(stageRoot, "Assets/Firebase/Plugins/Firebase.App.dll");
                if (!File.Exists(core))
                    preview.Errors.Add("Staging failed: Firebase.App.dll is missing.");
                string[] stagedManifests = Directory.GetFiles(stageRoot, "Firebase*_version-*_manifest.txt", SearchOption.AllDirectories);
                if (stagedManifests.Length == 0)
                    preview.Errors.Add("Staging failed: no Firebase version manifests were found.");
            }
            catch (Exception exception)
            {
                preview.Errors.Add("Staging failed: " + exception.Message);
            }
            return preview;
        }

        public static FirebaseInstallationResult PerformConfirmedUpdate(FirebaseArchiveInspection archive, FirebaseUpdatePreview preview, string stageRoot)
        {
            FirebaseInstallationResult result = new FirebaseInstallationResult { InstalledVersion = archive.Version };
            if (preview == null || !preview.IsValid || string.IsNullOrEmpty(stageRoot) || !Directory.Exists(stageRoot))
            {
                result.Errors.Add("The staged update is not valid. Build and validate the preview again.");
                return result;
            }

            try
            {
                EditorPrefs.SetBool(PendingInstallKey, true);
                AssetDatabase.StartAssetEditing();
                try
                {
                    foreach (FirebaseFileChange change in preview.Changes.Where(c => c.Kind == FirebaseFileChangeKind.Remove))
                        DeleteProjectPath(change.Path);
                    CopyDirectory(Path.Combine(stageRoot, "Assets"), "Assets");
                    ApplyGradleSpacePathCompatibility();
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                }

                foreach (FirebaseModuleInfo module in archive.Modules.Where(m => m.Selected))
                    result.InstalledModules.Add(module.Name);
                result.Messages.Add("Installed Firebase " + archive.Version + ": " + string.Join(", ", result.InstalledModules.ToArray()));
                result.Messages.Add("Android dependency resolution is queued after AssetDatabase refresh.");
                result.Success = true;
                EditorPrefs.SetBool(PendingInstallKey, false);
                EditorPrefs.SetBool(PendingResolverKey, true);
                EditorPrefs.SetString(ResolverStatusKey, "Queued");
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                EditorApplication.delayCall += CompletePendingResolution;
            }
            catch (Exception exception)
            {
                result.Errors.Add(exception.ToString());
            }
            finally
            {
                if (Directory.Exists(stageRoot))
                    Directory.Delete(stageRoot, true);
            }
            return result;
        }

        public static List<string> ValidateCurrentProject()
        {
            List<string> messages = new List<string>();
            FirebaseInstalledState installed = DetectInstalledFirebase();
            messages.Add("Installed Firebase: " + installed.Version + " (" + string.Join(", ", installed.Modules.ToArray()) + ")");
            messages.AddRange(installed.Warnings);
            if (EditorPrefs.GetBool(PendingInstallKey, false))
                messages.Add("A previous clean update was interrupted while applying files. Inspect the current version and rerun the clean update before building.");
            string resolverStatus = EditorPrefs.GetString(ResolverStatusKey, string.Empty);
            if (!string.IsNullOrEmpty(resolverStatus))
                messages.Add("Last Android resolver status: " + resolverStatus);

            string[] firebaseDlls = Directory.GetFiles("Assets", "Firebase*.dll", SearchOption.AllDirectories);
            foreach (IGrouping<string, string> group in firebaseDlls.GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                if (group.Count() > 1)
                    messages.Add("Duplicate DLL " + group.Key + ": " + string.Join(", ", group.ToArray()));
            }

            bool hasUpmResolver = File.ReadAllText("Packages/manifest.json").Contains("com.google.external-dependency-manager");
            bool hasAssetResolver = Directory.Exists("Assets/ExternalDependencyManager");
            if (hasUpmResolver && hasAssetResolver)
                messages.Add("Both UPM and Assets-based External Dependency Manager files are present. A clean update will preserve UPM and filter package copies.");

            const string template = "Assets/Plugins/Android/settingsTemplate.gradle";
            if (Application.dataPath.Contains(" ") && File.Exists(template) && !File.ReadAllText(template).Contains(".replace(\" \", \"%20\")"))
                messages.Add("Gradle repository URI does not encode spaces. This is corrected during a confirmed Firebase update.");
            return messages;
        }

        private static FirebaseModuleInfo InspectPackage(string packagePath, string packageName)
        {
            FirebaseModuleInfo module = new FirebaseModuleInfo
            {
                Name = NormalizeModuleName(Path.GetFileNameWithoutExtension(packageName)),
                PackagePath = packagePath
            };
            List<UnityPackageEntry> entries = UnityPackageReader.ReadEntries(packagePath);
            foreach (UnityPackageEntry entry in entries)
            {
                module.AssetPaths.Add(entry.Path);
                Match match = ManifestRegex.Match(Path.GetFileName(entry.Path));
                if (match.Success)
                    module.Version = match.Groups["version"].Value;
            }
            if (string.IsNullOrEmpty(module.Version))
                throw new InvalidDataException(packageName + " does not contain a Firebase version manifest.");
            return module;
        }

        private static void CompletePendingResolution()
        {
            if (!EditorPrefs.GetBool(PendingResolverKey, false))
                return;
            EditorPrefs.SetBool(PendingResolverKey, false);
            try
            {
                Type resolver = FindType("GooglePlayServices.PlayServicesResolver");
                MethodInfo method = resolver == null ? null : resolver.GetMethod("ResolveSync", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(bool) }, null);
                if (method == null)
                    throw new MissingMethodException("EDM4U PlayServicesResolver.ResolveSync(bool) was not found.");
                method.Invoke(null, new object[] { true });
                ApplyGradleSpacePathCompatibility();
                AssetDatabase.Refresh();
                EditorPrefs.SetString(ResolverStatusKey, "Completed");
                Debug.Log("[Firebase Updater] Firebase installation complete and Android dependencies resolved.");
            }
            catch (Exception exception)
            {
                EditorPrefs.SetString(ResolverStatusKey, "Failed: " + exception.GetBaseException().Message);
                Debug.LogError("[Firebase Updater] Firebase files were installed, but Android dependency resolution failed. Open Tools > Firebase > Installer / Updater for validation.\n" + exception);
            }
        }

        private static Type FindType(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, false);
                if (type != null)
                    return type;
            }
            return null;
        }

        private static void ApplyGradleSpacePathCompatibility()
        {
            const string template = "Assets/Plugins/Android/settingsTemplate.gradle";
            if (!File.Exists(template))
                return;
            string text = File.ReadAllText(template);
            const string encoded = ".replace(\" \", \"%20\")";
            if (text.Contains(encoded))
                return;
            const string marker = ".replace(\"\\\\\", \"/\")";
            if (text.Contains(marker))
                File.WriteAllText(template, text.Replace(marker, marker + encoded));
        }

        private static void DeleteProjectPath(string path)
        {
            string normalized = path.Replace('\\', '/');
            if (!IsSafeAssetPath(normalized) || ShouldPreserve(normalized) || ShouldFilter(normalized))
                return;
            if (File.Exists(normalized))
                File.Delete(normalized);
            else if (Directory.Exists(normalized))
                Directory.Delete(normalized, true);
            string meta = normalized + ".meta";
            if (File.Exists(meta))
                File.Delete(meta);
        }

        private static void CopyDirectory(string source, string destination)
        {
            if (!Directory.Exists(source))
                throw new DirectoryNotFoundException(source);
            foreach (string directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(destination + directory.Substring(source.Length));
            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                string target = destination + file.Substring(source.Length);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target, true);
            }
        }

        private static bool ShouldFilter(string path)
        {
            return path.Replace('\\', '/').StartsWith("Assets/ExternalDependencyManager/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ShouldPreserve(string path)
        {
            string fileName = Path.GetFileName(path);
            return PreservedFileNames.Any(name => string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsSafeAssetPath(string path)
        {
            return path.StartsWith("Assets/", StringComparison.Ordinal) && !path.Contains("../") && !path.Contains("/..");
        }

        private static void AddChange(FirebaseUpdatePreview preview, FirebaseFileChangeKind kind, string path, string reason)
        {
            if (!preview.Changes.Any(c => c.Kind == kind && string.Equals(c.Path, path, StringComparison.OrdinalIgnoreCase)))
                preview.Changes.Add(new FirebaseFileChange { Kind = kind, Path = path, Reason = reason });
        }

        private static string NormalizeModuleName(string value)
        {
            string name = value;
            if (name.StartsWith("Firebase", StringComparison.OrdinalIgnoreCase))
                name = name.Substring("Firebase".Length);
            return name == "RemoteConfig" ? "Remote Config" : name;
        }

        private static string ExtractVersionFromText(string value)
        {
            Match match = Regex.Match(value ?? string.Empty, @"(?<![0-9])([0-9]+\.[0-9]+\.[0-9]+)(?![0-9])");
            return match.Success ? match.Groups[1].Value : null;
        }

        private static string StablePathHash(string value)
        {
            using (System.Security.Cryptography.SHA256 hash = System.Security.Cryptography.SHA256.Create())
            {
                byte[] bytes = hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(value + File.GetLastWriteTimeUtc(value).Ticks));
                return BitConverter.ToString(bytes, 0, 8).Replace("-", string.Empty);
            }
        }

        private static void RecreateDirectory(string path)
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
            Directory.CreateDirectory(path);
        }
    }
}
#endif
