using System.IO;
using UnityEditor;
using UnityEngine;

namespace EpicPandaGames.EditorTools
{
    public sealed class ResizeTextureWindow : EditorWindow
    {
        private const int TargetSize = 1024;
        private Texture2D texture;

        [MenuItem("EPG/Tools/Resize Texture")]
        private static void Open() => GetWindow<ResizeTextureWindow>("Resize Texture");

        private void OnGUI()
        {
            texture = (Texture2D)EditorGUILayout.ObjectField("Texture", texture, typeof(Texture2D), false);
            EditorGUILayout.HelpBox("Overwrites the selected texture with a transparent 1024 x 1024 PNG.", MessageType.Warning);
            using (new EditorGUI.DisabledScope(texture == null))
                if (GUILayout.Button("Resize Texture")) Resize();
        }

        private void Resize()
        {
            string assetPath = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(assetPath)) return;
            float scale = Mathf.Min(TargetSize / (float)texture.width, TargetSize / (float)texture.height);
            int width = Mathf.RoundToInt(texture.width * scale);
            int height = Mathf.RoundToInt(texture.height * scale);
            RenderTexture target = RenderTexture.GetTemporary(TargetSize, TargetSize, 0, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            GL.Clear(true, true, Color.clear);
            Graphics.DrawTexture(new Rect((TargetSize - width) / 2f, (TargetSize - height) / 2f, width, height), texture);
            var result = new Texture2D(TargetSize, TargetSize, TextureFormat.RGBA32, false);
            result.ReadPixels(new Rect(0, 0, TargetSize, TargetSize), 0, 0);
            result.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            File.WriteAllBytes(assetPath, result.EncodeToPNG());
            DestroyImmediate(result);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        }
    }
}
