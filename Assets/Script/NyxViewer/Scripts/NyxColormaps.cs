// Gradient helpers: default colormaps and baking a Gradient into a 256x1 lookup texture.
using UnityEngine;

namespace Nyx
{
    public static class NyxColormaps
    {
        static Gradient Make(params string[] hex)
        {
            var keys = new GradientColorKey[hex.Length];
            for (int i = 0; i < hex.Length; i++)
            {
                ColorUtility.TryParseHtmlString(hex[i], out var c);
                keys[i] = new GradientColorKey(c, i / (float)(hex.Length - 1));
            }
            var g = new Gradient();
            g.SetKeys(keys, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
            return g;
        }

        /// <summary>Approximation of matplotlib "inferno" (8 keys, Unity's maximum).</summary>
        public static Gradient Inferno() =>
            Make("#000004", "#280B54", "#65156E", "#9F2A63", "#D44842", "#F57D15", "#FAC127", "#FCFFA4");

        /// <summary>Blue -> white -> red, good for temperature.</summary>
        public static Gradient CoolWarm() =>
            Make("#3B4CC0", "#6F92F3", "#AAC7FD", "#DDDDDD", "#F7B89C", "#E7745B", "#B40426");

        /// <summary>Approximation of matplotlib "viridis".</summary>
        public static Gradient Viridis() =>
            Make("#440154", "#46327E", "#365C8D", "#277F8E", "#1FA187", "#4AC16D", "#A0DA39", "#FDE725");

        /// <summary>True for null or Unity's default (white-to-white) gradient.</summary>
        public static bool IsUnset(Gradient g)
        {
            if (g == null) return true;
            var k = g.colorKeys;
            return k.Length == 2 && k[0].color == Color.white && k[1].color == Color.white;
        }

        public static T FindPlayer<T>() where T : Object
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindFirstObjectByType<T>();
#else
            return Object.FindObjectOfType<T>();
#endif
        }

        public static Texture2D Bake(Gradient g, Texture2D tex = null)
        {
            if (tex == null)
            {
                tex = new Texture2D(256, 1, TextureFormat.RGBA32, false, false)
                {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    name = "NyxColormapLUT"
                };
            }
            var px = new Color[256];
            for (int i = 0; i < 256; i++) px[i] = g.Evaluate(i / 255f);
            tex.SetPixels(px);
            tex.Apply(false, false);
            return tex;
        }
    }
}
