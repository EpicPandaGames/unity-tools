using System.IO;
using UnityEditor;
using UnityEngine;

namespace EpicPandaGames.EditorTools
{
    public enum TextureAlignment { Center, Left, Top, Right, Bottom, TopLeft, TopRight, BottomLeft, BottomRight }

    public sealed class ResizePow2Window : EditorWindow
    {
        private Texture2D texture;
        private TextureAlignment alignment = TextureAlignment.Center;

        [MenuItem("EPG/Tools/Resize Pow2")]
        private static void Open() => GetWindow<ResizePow2Window>("Resize Pow2");

        private void OnGUI()
        {
            texture = (Texture2D)EditorGUILayout.ObjectField("Texture", texture, typeof(Texture2D), false);
            alignment = (TextureAlignment)EditorGUILayout.EnumPopup("Alignment", alignment);
            EditorGUILayout.HelpBox("Overwrites the selected texture by padding it to a transparent square power-of-two PNG.", MessageType.Warning);
            using (new EditorGUI.DisabledScope(texture == null))
                if (GUILayout.Button("Resize Texture")) Resize();
        }

        private void Resize()
        {
            string assetPath = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(assetPath)) return;
            int size = Mathf.NextPowerOfTwo(Mathf.Max(texture.width, texture.height));
            GetOffset(size, texture.width, texture.height, alignment, out int x, out int y);
            RenderTexture target = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            GL.Clear(true, true, Color.clear);
            Graphics.DrawTexture(new Rect(x, size - y - texture.height, texture.width, texture.height), texture);
            var result = new Texture2D(size, size, TextureFormat.RGBA32, false);
            result.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            result.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            File.WriteAllBytes(assetPath, result.EncodeToPNG());
            DestroyImmediate(result);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        }

        private static void GetOffset(int size, int width, int height, TextureAlignment value, out int x, out int y)
        {
            x = value == TextureAlignment.Right || value == TextureAlignment.TopRight || value == TextureAlignment.BottomRight ? size - width
                : value == TextureAlignment.Left || value == TextureAlignment.TopLeft || value == TextureAlignment.BottomLeft ? 0 : (size - width) / 2;
            y = value == TextureAlignment.Top || value == TextureAlignment.TopLeft || value == TextureAlignment.TopRight ? size - height
                : value == TextureAlignment.Bottom || value == TextureAlignment.BottomLeft || value == TextureAlignment.BottomRight ? 0 : (size - height) / 2;
        }
    }
}
