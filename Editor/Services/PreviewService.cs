using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

namespace HMI.Workspace.Editor.Services
{
    public sealed class PreviewService : IPreviewService
    {
        private static readonly Dictionary<int, Texture2D> ThumbnailCache = new();
        private static readonly Dictionary<int, Texture2D> MaterialSwatchCache = new();
        private static readonly Dictionary<int, Texture2D> ModelThumbCache = new();

        public Texture2D GetThumbnail(Object asset) => GetBestThumbnail(asset);
        public Texture2D RenderInteractivePreview(Object asset, Vector2 previewSize) => GetBestThumbnail(asset);
        public void Dispose() { }

        public static Texture2D GetResolvedPreviewOrNull(Object asset)
        {
            if (asset == null)
                return null;

            var preview = AssetPreview.GetAssetPreview(asset);
            return IsUsablePreview(preview) ? preview : null;
        }

        public static Texture2D GetBestThumbnail(Object asset)
        {
            if (asset == null) return null;

            var id = asset.GetInstanceID();
            if (ThumbnailCache.TryGetValue(id, out var cached) && cached != null)
                return cached;

            if (asset is Texture2D texAsset)
                return texAsset;

            if (asset is Material material)
            {
                return CacheAndReturn(id, RenderMaterialThumbnail(material, 160));
            }

            if (asset is GameObject go)
            {
                return CacheAndReturn(id, RenderModelThumbnail(go, 160));
            }

            var generalPreview = GetResolvedPreviewOrNull(asset);
            if (IsUsablePreview(generalPreview))
                return CacheAndReturn(id, generalPreview);

            var generalMini = AssetPreview.GetMiniThumbnail(asset);
            if (IsUsablePreview(generalMini))
                return CacheAndReturn(id, generalMini);

            return EditorGUIUtility.FindTexture("d_DefaultAsset Icon");
        }

        private static Texture2D CacheAndReturn(int id, Texture2D texture)
        {
            if (texture != null)
                ThumbnailCache[id] = texture;
            return texture;
        }

        private static bool IsUsablePreview(Texture2D tex)
        {
            if (tex == null) return false;
            if (tex.width <= 1 || tex.height <= 1) return false;

            try
            {
                // 对材质/自定义 Shader 预览常见的纯黑图做兜底判断
                var pixels = tex.GetPixels32();
                if (pixels == null || pixels.Length == 0) return false;

                var step = Mathf.Max(1, pixels.Length / 32);
                float luminance = 0f;
                int samples = 0;
                for (var i = 0; i < pixels.Length; i += step)
                {
                    var p = pixels[i];
                    luminance += (0.2126f * p.r + 0.7152f * p.g + 0.0722f * p.b) / 255f;
                    samples++;
                }

                if (samples == 0) return false;
                var avg = luminance / samples;
                return avg > 0.045f;
            }
            catch
            {
                // 某些 Unity 预览纹理不可读；此时只要纹理存在就视为可用。
                return true;
            }
        }

        private static Texture2D GetMaterialSwatch(Material material)
        {
            var id = material.GetInstanceID();
            if (MaterialSwatchCache.TryGetValue(id, out var cached) && cached != null)
                return cached;

            var color = ResolveMaterialColor(material);
            var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                name = $"Swatch_{material.name}"
            };

            var border = new Color(
                Mathf.Clamp01(color.r * 0.65f),
                Mathf.Clamp01(color.g * 0.65f),
                Mathf.Clamp01(color.b * 0.65f),
                1f);

            for (var y = 0; y < tex.height; y++)
            {
                for (var x = 0; x < tex.width; x++)
                {
                    var isBorder = x < 3 || x > tex.width - 4 || y < 3 || y > tex.height - 4;
                    tex.SetPixel(x, y, isBorder ? border : color);
                }
            }

            tex.Apply(false, true);
            MaterialSwatchCache[id] = tex;
            return tex;
        }

        private static Texture2D RenderMaterialThumbnail(Material material, int size)
        {
            var id = material.GetInstanceID();
            if (MaterialSwatchCache.TryGetValue(id, out var cached) && cached != null)
                return cached;

            var primary = ResolveMaterialThumbnailColor(material);
            var secondary = BuildSecondaryColor(primary);
            var accent = BuildAccentColor(primary);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                name = $"MatThumb_{material.name}"
            };

            var bgA = new Color(0.15f, 0.16f, 0.19f, 1f);
            var bgB = new Color(0.09f, 0.1f, 0.12f, 1f);
            var inset = Mathf.Max(6, size / 18);
            var accentHeight = Mathf.Max(10, size / 9);

            for (var y = 0; y < size; y++)
            {
                var t = y / (float)(size - 1);
                var rowBg = Color.Lerp(bgA, bgB, t);

                for (var x = 0; x < size; x++)
                {
                    var pixel = rowBg;
                    var inside = x >= inset && x < size - inset && y >= inset && y < size - inset;
                    if (inside)
                    {
                        var ux = (x - inset) / (float)Mathf.Max(1, size - inset * 2 - 1);
                        var uy = (y - inset) / (float)Mathf.Max(1, size - inset * 2 - 1);
                        pixel = Color.Lerp(primary, secondary, uy * 0.7f + ux * 0.3f);

                        var stripe = Mathf.Sin((ux * 3.5f + uy * 1.8f) * Mathf.PI * 2f);
                        if (stripe > 0.55f)
                            pixel = Color.Lerp(pixel, accent, 0.22f);

                        var metallicBand = Mathf.Abs(ux - 0.5f);
                        if (metallicBand < 0.08f)
                            pixel = Color.Lerp(pixel, Color.white, 0.18f * (1f - metallicBand / 0.08f));

                        var vignetteX = Mathf.Abs(ux - 0.5f) * 2f;
                        var vignetteY = Mathf.Abs(uy - 0.5f) * 2f;
                        var vignette = Mathf.Clamp01(1f - (vignetteX * vignetteX + vignetteY * vignetteY) * 0.25f);
                        pixel *= Mathf.Lerp(0.88f, 1.08f, vignette);

                        if (y >= size - inset - accentHeight)
                            pixel = Color.Lerp(pixel, accent, 0.35f);
                    }

                    if (x < 2 || x >= size - 2 || y < 2 || y >= size - 2)
                        pixel = new Color(0.22f, 0.24f, 0.29f, 1f);

                    tex.SetPixel(x, y, pixel);
                }
            }

            tex.Apply(false, true);
            MaterialSwatchCache[id] = tex;
            return tex;
        }

        private static Texture2D RenderModelThumbnail(GameObject prefabOrModel, int size)
        {
            var id = prefabOrModel.GetInstanceID();
            if (ModelThumbCache.TryGetValue(id, out var cached) && cached != null)
                return cached;

            var accent = BuildHashedFallbackColor(prefabOrModel.name);
            var accent2 = BuildSecondaryColor(accent);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                name = $"ModelThumb_{prefabOrModel.name}"
            };

            FillBackground(tex, new Color(0.14f, 0.15f, 0.18f, 1f), new Color(0.08f, 0.09f, 0.11f, 1f));
            DrawModelGlyph(tex, prefabOrModel.name, accent, accent2);
            DrawFrame(tex, new Color(0.2f, 0.22f, 0.27f, 1f));
            tex.Apply(false, true);
            ModelThumbCache[id] = tex;
            return tex;
        }

        private static void FillBackground(Texture2D tex, Color top, Color bottom)
        {
            for (var y = 0; y < tex.height; y++)
            {
                var t = y / (float)(tex.height - 1);
                var row = Color.Lerp(top, bottom, t);
                for (var x = 0; x < tex.width; x++)
                    tex.SetPixel(x, y, row);
            }
        }

        private static void DrawFrame(Texture2D tex, Color frame)
        {
            for (var x = 0; x < tex.width; x++)
            {
                tex.SetPixel(x, 0, frame);
                tex.SetPixel(x, tex.height - 1, frame);
            }
            for (var y = 0; y < tex.height; y++)
            {
                tex.SetPixel(0, y, frame);
                tex.SetPixel(tex.width - 1, y, frame);
            }
        }

        private static void DrawModelGlyph(Texture2D tex, string name, Color accent, Color accent2)
        {
            var lower = (name ?? string.Empty).ToLowerInvariant();
            if (lower.Contains("car") || lower.Contains("bus") || lower.Contains("ambulance"))
            {
                DrawCarGlyph(tex, accent, accent2);
                return;
            }
            if (lower.Contains("building") || lower.Contains("roof") || lower.Contains("foundation"))
            {
                DrawBuildingGlyph(tex, accent, accent2);
                return;
            }
            if (lower.Contains("cone") || lower.Contains("pillar"))
            {
                DrawConeGlyph(tex, accent, accent2);
                return;
            }
            if (lower.Contains("sign") || lower.Contains("barrier") || lower.Contains("gate"))
            {
                DrawSignGlyph(tex, accent, accent2);
                return;
            }

            DrawBlockGlyph(tex, accent, accent2);
        }

        private static void DrawCarGlyph(Texture2D tex, Color accent, Color accent2)
        {
            FillRoundedRect(tex, 18, 64, tex.width - 18, 112, accent, 10);
            FillRoundedRect(tex, 42, 44, tex.width - 42, 78, accent2, 12);
            FillRect(tex, 52, 56, tex.width - 52, 72, new Color(0.82f, 0.9f, 0.98f, 1f));
            FillCircle(tex, 42, 112, 12, new Color(0.1f, 0.11f, 0.14f, 1f));
            FillCircle(tex, tex.width - 42, 112, 12, new Color(0.1f, 0.11f, 0.14f, 1f));
        }

        private static void DrawBuildingGlyph(Texture2D tex, Color accent, Color accent2)
        {
            FillRoundedRect(tex, 30, 34, tex.width - 30, tex.height - 24, accent, 8);
            FillRoundedRect(tex, 48, 20, tex.width - 48, tex.height - 44, accent2, 8);
            for (var y = 46; y < tex.height - 36; y += 18)
            {
                for (var x = 44; x < tex.width - 44; x += 18)
                    FillRect(tex, x, y, x + 8, y + 10, new Color(0.92f, 0.95f, 1f, 0.55f));
            }
        }

        private static void DrawConeGlyph(Texture2D tex, Color accent, Color accent2)
        {
            FillTriangle(tex, new Vector2(tex.width / 2f, 26), new Vector2(40, tex.height - 28), new Vector2(tex.width - 40, tex.height - 28), accent);
            FillRect(tex, 34, tex.height - 38, tex.width - 34, tex.height - 22, accent2);
            FillRect(tex, 52, tex.height - 72, tex.width - 52, tex.height - 56, new Color(0.96f, 0.98f, 1f, 0.4f));
        }

        private static void DrawSignGlyph(Texture2D tex, Color accent, Color accent2)
        {
            FillRoundedRect(tex, 32, 26, tex.width - 32, 82, accent, 8);
            FillRect(tex, tex.width / 2 - 5, 82, tex.width / 2 + 5, tex.height - 26, accent2);
            FillRect(tex, tex.width / 2 - 28, tex.height - 34, tex.width / 2 + 28, tex.height - 22, accent2);
        }

        private static void DrawBlockGlyph(Texture2D tex, Color accent, Color accent2)
        {
            FillRoundedRect(tex, 26, 34, tex.width - 26, tex.height - 30, accent, 10);
            FillRect(tex, 42, 50, tex.width - 42, tex.height - 46, accent2);
            FillRect(tex, 26, 76, tex.width - 26, 88, new Color(0.97f, 0.99f, 1f, 0.18f));
        }

        private static void FillRect(Texture2D tex, int x0, int y0, int x1, int y1, Color color)
        {
            x0 = Mathf.Clamp(x0, 0, tex.width);
            x1 = Mathf.Clamp(x1, 0, tex.width);
            y0 = Mathf.Clamp(y0, 0, tex.height);
            y1 = Mathf.Clamp(y1, 0, tex.height);
            for (var y = y0; y < y1; y++)
                for (var x = x0; x < x1; x++)
                    tex.SetPixel(x, y, color);
        }

        private static void FillRoundedRect(Texture2D tex, int x0, int y0, int x1, int y1, Color color, int radius)
        {
            for (var y = y0; y < y1; y++)
            {
                for (var x = x0; x < x1; x++)
                {
                    var dx = Mathf.Min(x - x0, x1 - 1 - x);
                    var dy = Mathf.Min(y - y0, y1 - 1 - y);
                    if (dx >= radius || dy >= radius)
                    {
                        tex.SetPixel(x, y, color);
                        continue;
                    }

                    var cx = radius - dx - 1;
                    var cy = radius - dy - 1;
                    if (cx * cx + cy * cy <= radius * radius)
                        tex.SetPixel(x, y, color);
                }
            }
        }

        private static void FillCircle(Texture2D tex, int cx, int cy, int radius, Color color)
        {
            var r2 = radius * radius;
            for (var y = cy - radius; y <= cy + radius; y++)
            {
                for (var x = cx - radius; x <= cx + radius; x++)
                {
                    var dx = x - cx;
                    var dy = y - cy;
                    if (dx * dx + dy * dy <= r2 && x >= 0 && x < tex.width && y >= 0 && y < tex.height)
                        tex.SetPixel(x, y, color);
                }
            }
        }

        private static void FillTriangle(Texture2D tex, Vector2 a, Vector2 b, Vector2 c, Color color)
        {
            var minX = Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)));
            var maxX = Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x)));
            var minY = Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y)));
            var maxY = Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y)));

            for (var y = minY; y <= maxY; y++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    if (PointInTriangle(p, a, b, c) && x >= 0 && x < tex.width && y >= 0 && y < tex.height)
                        tex.SetPixel(x, y, color);
                }
            }
        }

        private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            var s1 = Sign(p, a, b);
            var s2 = Sign(p, b, c);
            var s3 = Sign(p, c, a);
            var hasNeg = (s1 < 0) || (s2 < 0) || (s3 < 0);
            var hasPos = (s1 > 0) || (s2 > 0) || (s3 > 0);
            return !(hasNeg && hasPos);
        }

        private static float Sign(Vector2 p1, Vector2 p2, Vector2 p3)
        {
            return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
        }

        private static Color ResolveMaterialColor(Material material)
        {
            if (TryGetColor(material, "_BaseColor", out var baseColor) && IsMeaningfulColor(baseColor))
                return NormalizePreviewColor(baseColor);
            if (TryGetColor(material, "_Color", out var color) && IsMeaningfulColor(color))
                return NormalizePreviewColor(color);
            if (TryGetColor(material, "_Tint", out var tint) && IsMeaningfulColor(tint))
                return NormalizePreviewColor(tint);

            if (TryGetBestShaderColor(material, out var shaderColor))
                return NormalizePreviewColor(shaderColor);

            return BuildHashedFallbackColor(material);
        }

        private static Color ResolveMaterialThumbnailColor(Material material)
        {
            var lowerName = material.name.ToLowerInvariant();
            if (lowerName.Contains("black") || lowerName.Contains("shadow") || lowerName.Contains("carbon"))
                return new Color(0.28f, 0.3f, 0.34f, 1f);
            if (lowerName.Contains("white"))
                return new Color(0.85f, 0.87f, 0.9f, 1f);
            if (lowerName.Contains("red"))
                return new Color(0.76f, 0.26f, 0.28f, 1f);
            if (lowerName.Contains("green"))
                return new Color(0.28f, 0.62f, 0.4f, 1f);
            if (lowerName.Contains("blue"))
                return new Color(0.27f, 0.46f, 0.78f, 1f);
            if (lowerName.Contains("beige") || lowerName.Contains("chocolat") || lowerName.Contains("brown"))
                return new Color(0.7f, 0.58f, 0.42f, 1f);
            if (lowerName.Contains("chrome") || lowerName.Contains("metal") || lowerName.Contains("wheel"))
                return new Color(0.68f, 0.72f, 0.78f, 1f);
            if (lowerName.Contains("glass"))
                return new Color(0.48f, 0.72f, 0.82f, 1f);
            if (lowerName.Contains("light"))
                return new Color(0.82f, 0.62f, 0.2f, 1f);

            var resolved = ResolveMaterialColor(material);
            if (GetLuminance(resolved) > 0.14f)
                return resolved;

            return BuildHashedFallbackColor(material);
        }

        private static bool TryGetColor(Material material, string propertyName, out Color color)
        {
            color = default;
            if (!material.HasProperty(propertyName))
                return false;

            color = material.GetColor(propertyName);
            return true;
        }

        private static bool TryGetBestShaderColor(Material material, out Color color)
        {
            color = default;
            var shader = material.shader;
            if (shader == null)
                return false;

            var propertyCount = ShaderUtil.GetPropertyCount(shader);
            var bestScore = float.MinValue;
            var found = false;

            for (var i = 0; i < propertyCount; i++)
            {
                var type = ShaderUtil.GetPropertyType(shader, i);
                if (type != ShaderUtil.ShaderPropertyType.Color && type != ShaderUtil.ShaderPropertyType.Vector)
                    continue;

                var propertyName = ShaderUtil.GetPropertyName(shader, i);
                if (!material.HasProperty(propertyName))
                    continue;

                Color candidate;
                try
                {
                    candidate = material.GetColor(propertyName);
                }
                catch
                {
                    continue;
                }

                var score = ScoreColorProperty(propertyName, candidate);
                if (score <= bestScore)
                    continue;

                bestScore = score;
                color = candidate;
                found = true;
            }

            return found && IsMeaningfulColor(color);
        }

        private static float ScoreColorProperty(string propertyName, Color color)
        {
            var lowerName = propertyName.ToLowerInvariant();
            var luminance = GetLuminance(color);
            var saturation = GetSaturation(color);
            var score = luminance + saturation * 0.35f;

            if (lowerName.Contains("base")) score += 0.6f;
            if (lowerName.Contains("albedo")) score += 0.55f;
            if (lowerName.Contains("color")) score += 0.5f;
            if (lowerName.Contains("tint")) score += 0.45f;
            if (lowerName.Contains("diffuse")) score += 0.35f;
            if (lowerName.Contains("emission")) score -= 0.2f;

            return score;
        }

        private static bool IsMeaningfulColor(Color color)
        {
            return color.a > 0.01f && GetLuminance(color) > 0.06f;
        }

        private static Color NormalizePreviewColor(Color color)
        {
            Color.RGBToHSV(color, out var h, out var s, out var v);
            s = Mathf.Clamp(s, 0.18f, 0.85f);
            v = Mathf.Clamp(v, 0.38f, 0.92f);
            var normalized = Color.HSVToRGB(h, s, v);
            normalized.a = 1f;
            return normalized;
        }

        private static Color BuildHashedFallbackColor(Material material)
        {
            return BuildHashedFallbackColor(material.shader != null ? material.shader.name : material.name);
        }

        private static Color BuildHashedFallbackColor(string seed)
        {
            var hash = Mathf.Abs((seed ?? "asset").GetHashCode());
            var hue = (hash % 360) / 360f;
            var saturation = 0.45f + ((hash / 360 % 100) / 100f) * 0.25f;
            var value = 0.55f + ((hash / 36000 % 100) / 100f) * 0.2f;
            var fallback = Color.HSVToRGB(hue, saturation, value);
            fallback.a = 1f;
            return fallback;
        }

        private static Color BuildSecondaryColor(Color primary)
        {
            Color.RGBToHSV(primary, out var h, out var s, out var v);
            var shifted = Color.HSVToRGB(Mathf.Repeat(h + 0.035f, 1f), Mathf.Clamp01(s * 0.75f), Mathf.Clamp01(v * 0.82f));
            shifted.a = 1f;
            return shifted;
        }

        private static Color BuildAccentColor(Color primary)
        {
            Color.RGBToHSV(primary, out var h, out var s, out var v);
            var accent = Color.HSVToRGB(Mathf.Repeat(h - 0.02f, 1f), Mathf.Clamp(s + 0.12f, 0.2f, 0.95f), Mathf.Clamp(v + 0.08f, 0.45f, 1f));
            accent.a = 1f;
            return accent;
        }

        private static float GetLuminance(Color color)
        {
            return 0.2126f * color.r + 0.7152f * color.g + 0.0722f * color.b;
        }

        private static float GetSaturation(Color color)
        {
            var max = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
            var min = Mathf.Min(color.r, Mathf.Min(color.g, color.b));
            return max <= 0f ? 0f : (max - min) / max;
        }
    }
}
