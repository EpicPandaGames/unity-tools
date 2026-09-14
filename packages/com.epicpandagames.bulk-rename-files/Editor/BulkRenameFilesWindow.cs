using UnityEditor;
using UnityEngine;

namespace EpicPandaGames.EditorTools
{
    public sealed class BulkRenameFilesWindow : EditorWindow
    {
        private DefaultAsset folder;
        private string prefix = "";
        private int startIndex;

        [MenuItem("EPG/Tools/Bulk Rename Files")]
        private static void Open() => GetWindow<BulkRenameFilesWindow>("Bulk Rename Files");

        private void OnGUI()
        {
            folder = (DefaultAsset)EditorGUILayout.ObjectField("Folder", folder, typeof(DefaultAsset), false);
            prefix = EditorGUILayout.TextField("New Name Prefix", prefix);
            startIndex = EditorGUILayout.IntField("Start Index", startIndex);
            using (new EditorGUI.DisabledScope(folder == null))
                if (GUILayout.Button("Rename")) Rename();
        }

        private void Rename()
        {
            string folderPath = AssetDatabase.GetAssetPath(folder);
            if (!AssetDatabase.IsValidFolder(folderPath)) { Debug.LogError("Select a valid project folder."); return; }
            string[] guids = AssetDatabase.FindAssets("", new[] { folderPath });
            int index = startIndex;
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.IsValidFolder(path)) continue;
                string error = AssetDatabase.RenameAsset(path, $"{prefix}{index++}");
                if (!string.IsNullOrEmpty(error)) Debug.LogError($"Could not rename {path}: {error}");
            }
            AssetDatabase.SaveAssets();
        }
    }
}
