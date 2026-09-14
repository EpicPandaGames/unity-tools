using System.IO;
using UnityEditor;
using UnityEngine;

namespace EpicPandaGames.EditorTools
{
    public sealed class BulkRenameFoldersWindow : EditorWindow
    {
        private DefaultAsset folder;
        private string prefix = "";
        private int startIndex;
        private bool renameTextures;

        [MenuItem("EPG/Tools/Bulk Rename Folders")]
        private static void Open() => GetWindow<BulkRenameFoldersWindow>("Bulk Rename Folders");

        private void OnGUI()
        {
            folder = (DefaultAsset)EditorGUILayout.ObjectField("Parent Folder", folder, typeof(DefaultAsset), false);
            prefix = EditorGUILayout.TextField("New Name Prefix", prefix);
            startIndex = EditorGUILayout.IntField("Start Index", startIndex);
            renameTextures = EditorGUILayout.Toggle("Rename Textures Inside", renameTextures);
            using (new EditorGUI.DisabledScope(folder == null))
                if (GUILayout.Button("Rename")) Rename();
        }

        private void Rename()
        {
            string root = AssetDatabase.GetAssetPath(folder);
            if (!AssetDatabase.IsValidFolder(root)) { Debug.LogError("Select a valid project folder."); return; }
            string[] guids = AssetDatabase.FindAssets("t:Folder", new[] { root });
            int index = startIndex;
            foreach (string guid in guids)
            {
                string oldPath = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetDirectoryName(oldPath)?.Replace('\\', '/') != root) continue;
                string newName = $"{prefix}{index++}";
                string error = AssetDatabase.RenameAsset(oldPath, newName);
                if (!string.IsNullOrEmpty(error)) { Debug.LogError($"Could not rename {oldPath}: {error}"); continue; }
                if (renameTextures) RenameTextures($"{root}/{newName}");
            }
            AssetDatabase.SaveAssets();
        }

        private static void RenameTextures(string folderPath)
        {
            int index = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Texture", new[] { folderPath }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string error = AssetDatabase.RenameAsset(path, (index++).ToString());
                if (!string.IsNullOrEmpty(error)) Debug.LogError($"Could not rename {path}: {error}");
            }
        }
    }
}
