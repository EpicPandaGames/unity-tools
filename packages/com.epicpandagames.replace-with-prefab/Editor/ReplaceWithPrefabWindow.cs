using UnityEditor;
using UnityEngine;

namespace EpicPandaGames.EditorTools
{
    public sealed class ReplaceWithPrefabWindow : EditorWindow
    {
        private GameObject prefab;

        [MenuItem("EPG/Tools/Replace With Prefab")]
        private static void Open() => GetWindow<ReplaceWithPrefabWindow>("Replace With Prefab");

        private void OnGUI()
        {
            prefab = (GameObject)EditorGUILayout.ObjectField("Prefab", prefab, typeof(GameObject), false);
            using (new EditorGUI.DisabledScope(prefab == null || Selection.gameObjects.Length == 0))
                if (GUILayout.Button("Replace")) Replace();
        }

        private void Replace()
        {
            foreach (GameObject source in Selection.gameObjects)
            {
                GameObject replacement = (GameObject)PrefabUtility.InstantiatePrefab(prefab, source.scene);
                Undo.RegisterCreatedObjectUndo(replacement, "Replace With Prefab");
                Transform transform = replacement.transform;
                transform.SetParent(source.transform.parent, false);
                transform.SetSiblingIndex(source.transform.GetSiblingIndex());
                transform.localPosition = source.transform.localPosition;
                transform.localRotation = source.transform.localRotation;
                transform.localScale = source.transform.localScale;
                replacement.name = source.name;
                Undo.DestroyObjectImmediate(source);
            }
        }
    }
}
