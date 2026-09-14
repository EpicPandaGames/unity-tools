using UnityEditor;
using UnityEngine;

namespace EpicPandaGames.EditorTools
{
    public sealed class RenameSelectedGameObjectsWindow : EditorWindow
    {
        private string prefix = "Object";
        private int startIndex = 1;

        [MenuItem("EPG/Tools/Rename Selected GameObjects")]
        private static void Open() => GetWindow<RenameSelectedGameObjectsWindow>("Rename GameObjects");

        private void OnGUI()
        {
            prefix = EditorGUILayout.TextField("Prefix", prefix);
            startIndex = EditorGUILayout.IntField("Start Index", startIndex);
            using (new EditorGUI.DisabledScope(Selection.gameObjects.Length == 0))
                if (GUILayout.Button("Rename Selected")) Rename();
        }

        private void Rename()
        {
            GameObject[] objects = Selection.gameObjects;
            Undo.RecordObjects(objects, "Rename Selected GameObjects");
            for (int i = 0; i < objects.Length; i++) objects[i].name = $"{prefix} {startIndex + i}";
        }
    }
}
