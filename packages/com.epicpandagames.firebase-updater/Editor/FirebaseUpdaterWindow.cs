#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace EpicPandaGames.UnityTools.FirebaseUpdater
{
    internal sealed class FirebaseUpdaterWindow : EditorWindow
    {
        private FirebaseInstalledState installed;
        private FirebaseArchiveInspection archive;
        private FirebaseUpdatePreview preview;
        private FirebaseInstallationResult result;
        private Vector2 scroll;
        private bool showChanges = true;
        private bool showValidation = true;
        private List<string> validationMessages = new List<string>();
        private string stagedRoot;

        [MenuItem("Tools/Epic Panda Games/Firebase Updater")]
        private static void Open()
        {
            FirebaseUpdaterWindow window = GetWindow<FirebaseUpdaterWindow>();
            window.titleContent = new GUIContent("Firebase Updater");
            window.minSize = new Vector2(680f, 520f);
            window.RefreshInstalledState();
            window.Show();
        }

        private void OnEnable()
        {
            RefreshInstalledState();
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawHeader();
            DrawInstalledState();
            DrawArchiveSelection();
            if (archive != null)
                DrawArchive();
            if (preview != null)
                DrawPreview();
            if (result != null)
                DrawResult();
            DrawValidation();
            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Firebase Installer / Updater", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Inspection and preview are read-only. Project files change only after staging succeeds and you confirm the exact clean-update summary.",
                MessageType.Info);
        }

        private void DrawInstalledState()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Current Project", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("SDK version", installed == null ? "Scanning..." : installed.Version);
            EditorGUILayout.LabelField("Modules", installed == null || installed.Modules.Count == 0 ? "None detected" : string.Join(", ", installed.Modules.ToArray()));
            if (GUILayout.Button("Rescan Project", GUILayout.Width(140f)))
                RefreshInstalledState();
        }

        private void DrawArchiveSelection()
        {
            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("SDK Archive", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.SelectableLabel(archive == null ? "No ZIP selected" : archive.ZipPath, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                if (GUILayout.Button("Select ZIP...", GUILayout.Width(110f)))
                {
                    string initial = archive == null ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) : Path.GetDirectoryName(archive.ZipPath);
                    string path = EditorUtility.OpenFilePanel("Select Firebase Unity SDK ZIP", initial, "zip");
                    if (!string.IsNullOrEmpty(path))
                        Inspect(path);
                }
            }
        }

        private void DrawArchive()
        {
            EditorGUILayout.LabelField("Content version", archive.Version);
            DrawMessages(archive.Errors, MessageType.Error);
            DrawMessages(archive.Warnings, MessageType.Warning);
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Modules", EditorStyles.boldLabel);

            EditorGUI.BeginDisabledGroup(!archive.IsValid);
            foreach (FirebaseModuleInfo module in archive.Modules)
            {
                bool selected = EditorGUILayout.ToggleLeft(
                    module.Name + "  " + module.Version + (module.Installed ? "  (installed)" : string.Empty), module.Selected);
                if (selected != module.Selected)
                {
                    module.Selected = selected;
                    preview = null;
                    ClearStage();
                }
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(6f);
            EditorGUI.BeginDisabledGroup(!archive.IsValid || !archive.Modules.Any(m => m.Selected));
            if (GUILayout.Button("Build Read-Only Preview", GUILayout.Height(30f)))
            {
                ClearStage();
                preview = FirebaseUpdaterService.BuildUpdatePreview(archive, installed);
                result = null;
            }
            EditorGUI.EndDisabledGroup();
        }

        private void DrawPreview()
        {
            EditorGUILayout.Space(12f);
            EditorGUILayout.LabelField("Clean Update Preview", EditorStyles.boldLabel);
            DrawMessages(preview.Errors, MessageType.Error);
            DrawMessages(preview.Warnings, MessageType.Warning);

            int add = preview.Changes.Count(c => c.Kind == FirebaseFileChangeKind.Add);
            int replace = preview.Changes.Count(c => c.Kind == FirebaseFileChangeKind.Replace);
            int remove = preview.Changes.Count(c => c.Kind == FirebaseFileChangeKind.Remove);
            int filtered = preview.Changes.Count(c => c.Kind == FirebaseFileChangeKind.Filter);
            EditorGUILayout.LabelField("Summary", add + " add, " + replace + " replace, " + remove + " remove, " + filtered + " resolver files filtered");

            showChanges = EditorGUILayout.Foldout(showChanges, "Exact file changes (" + preview.Changes.Count + ")", true);
            if (showChanges)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    foreach (FirebaseFileChange change in preview.Changes)
                        EditorGUILayout.LabelField("[" + change.Kind + "] " + change.Path, change.Reason, EditorStyles.miniLabel);
                }
            }

            EditorGUILayout.Space(8f);
            EditorGUI.BeginDisabledGroup(!preview.IsValid);
            GUI.backgroundColor = new Color(1f, 0.65f, 0.25f);
            if (GUILayout.Button("Validate, Review Confirmation, and Clean Update", GUILayout.Height(34f)))
                ConfirmAndInstall();
            GUI.backgroundColor = Color.white;
            EditorGUI.EndDisabledGroup();
        }

        private void DrawResult()
        {
            EditorGUILayout.Space(12f);
            EditorGUILayout.LabelField("Last Installation Result", EditorStyles.boldLabel);
            DrawMessages(result.Errors, MessageType.Error);
            DrawMessages(result.Messages, result.Success ? MessageType.Info : MessageType.Warning);
        }

        private void DrawValidation()
        {
            EditorGUILayout.Space(12f);
            showValidation = EditorGUILayout.Foldout(showValidation, "Project Validation", true);
            if (!showValidation)
                return;
            if (GUILayout.Button("Validate Current Firebase Installation", GUILayout.Width(260f)))
                validationMessages = FirebaseUpdaterService.ValidateCurrentProject();
            foreach (string message in validationMessages)
                EditorGUILayout.HelpBox(message, message.StartsWith("Installed Firebase", StringComparison.Ordinal) ? MessageType.Info : MessageType.Warning);
        }

        private void ConfirmAndInstall()
        {
            try
            {
                EditorUtility.DisplayProgressBar("Firebase Updater", "Validating and staging selected packages...", 0.25f);
                ClearStage();
                FirebaseUpdaterService.ValidateStagedUpdate(archive, preview, out stagedRoot);
                if (!preview.IsValid)
                    return;

                int removals = preview.Changes.Count(c => c.Kind == FirebaseFileChangeKind.Remove);
                int replacements = preview.Changes.Count(c => c.Kind == FirebaseFileChangeKind.Replace);
                string modules = string.Join(", ", archive.Modules.Where(m => m.Selected).Select(m => m.Name).ToArray());
                bool confirmed = EditorUtility.DisplayDialog(
                    "Confirm Firebase Clean Update",
                    "Install Firebase " + archive.Version + " modules:\n" + modules +
                    "\n\nThis will remove " + removals + " obsolete paths and replace " + replacements +
                    " existing paths. Configuration files and UPM EDM4U are preserved. No rollback backup will be retained.",
                    "Clean Update", "Cancel");
                if (!confirmed)
                    return;

                EditorUtility.DisplayProgressBar("Firebase Updater", "Applying confirmed Firebase update...", 0.75f);
                result = FirebaseUpdaterService.PerformConfirmedUpdate(archive, preview, stagedRoot);
                stagedRoot = null;
                RefreshInstalledState();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private void Inspect(string path)
        {
            try
            {
                EditorUtility.DisplayProgressBar("Firebase Updater", "Inspecting SDK archive...", 0.5f);
                archive = FirebaseUpdaterService.InspectArchive(path);
                preview = null;
                result = null;
                ClearStage();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private void RefreshInstalledState()
        {
            installed = FirebaseUpdaterService.DetectInstalledFirebase();
            validationMessages = FirebaseUpdaterService.ValidateCurrentProject();
            Repaint();
        }

        private static void DrawMessages(IEnumerable<string> messages, MessageType type)
        {
            foreach (string message in messages)
                EditorGUILayout.HelpBox(message, type);
        }

        private void ClearStage()
        {
            if (!string.IsNullOrEmpty(stagedRoot) && Directory.Exists(stagedRoot))
                Directory.Delete(stagedRoot, true);
            stagedRoot = null;
        }
    }
}
#endif
