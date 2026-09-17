// ============================================================================
// LedTextMaskBaker.cs
// ----------------------------------------------------------------------------
// 把一段文字烘焙成"LED 点阵遮罩"纹理，喂给 CartoonRendering/LED/DotMatrix Text。
//
// 为什么不用 TMP + RenderTexture：
//   走的是 Font.GetCharacterInfo 那条稳定的公开 API，把字形位图直接拼到一张 Texture2D 上。
//   不依赖任何字体资产、不用额外的相机和 RT，输出就是一张普通的单通道遮罩图，
//   在 Inspector 里能直接看到、能存成 PNG 慢慢调。
//
// 用法：
//   1) 挂在 LED 屏物体上（同物体最好有 LedCurvedScreen，宽高比会自动对齐）
//   2) 填 targetMaterials 或让它自动抓同物体的 Renderer 材质
//   3) 改文字 → 右键组件 [Rebake]，或勾 bakeOnEnable 让它在 OnEnable 时自己烘
//
// 下里面两个 Unity 坑：
//   - 字体图集是单通道（Alpha8 / R8）且 isReadable，直接 GetRawTextureData 最快；
//     早先走 GPU blit 回读拿到的是恒定 0.5 的覆盖率，字形全变实心方块
//   - 图集里字形是【上下倒着】存的，必须反转源行（sy + sh - 1 - y），
//     而不是挪矩形位置

using System.Collections.Generic;
using UnityEngine;

namespace CartoonRendering
{
    [ExecuteAlways]
    public sealed class LedTextMaskBaker : MonoBehaviour
    {
        [Header("文本")]
        [Tooltip("支持 \\n 换行；单行会水平居中")]
        [SerializeField, TextArea(1, 4)] string text = "SYSTEM ONLINE";

        [Tooltip("系统字体名。留空则用 Unity 内置字体。中文可以填 Microsoft YaHei / SimHei")]
        [SerializeField] string osFontName = "Arial";

        [SerializeField] FontStyle fontStyle = FontStyle.Bold;
        [Tooltip("额外的字间距（像素，作用在烘焙尺寸上）")]
        [SerializeField] float letterSpacing = 0f;

        [Header("烘焙")]
        [SerializeField] int textureWidth = 2048;
        [SerializeField] int textureHeight = 512;

        [Tooltip("是否按同物体上 LedCurvedScreen 的宽高比自动修正 textureHeight")]
        [SerializeField] bool matchScreenAspect = true;

        [Tooltip("四周留白，相对于纹理高度的比例")]
        [SerializeField, Range(0f, 0.4f)] float padding = 0.12f;

        [Tooltip("字号上限，防止极端窄长文本把字号推爆")]
        [SerializeField] int maxFontSize = 512;

        [Header("输出")]
        [Tooltip("留空则自动使用同物体 Renderer 上的共享材质")]
        [SerializeField] List<Material> targetMaterials = new List<Material>();

        [SerializeField] string textureProperty = "_MainTex";
        [SerializeField] string aspectProperty = "_Aspect";
        [SerializeField] bool setAspectProperty = true;

        [Header("时机")]
        [SerializeField] bool bakeOnEnable = true;

        /// <summary>最近一次烘焙出来的遮罩（单通道 R8）。未烘焙时为 null。</summary>
        public Texture2D Mask => _mask;

        /// <summary>最近一次烘焙时读到的字体图集拷贝（诊断用）。</summary>
        public Texture2D LastAtlasCopy { get; private set; }

        /// <summary>最近一次烘焙用的字体图集格式描述（诊断用）。</summary>
        public string LastAtlasInfo { get; private set; }

        Texture2D _mask;
        float _lastAspect;
        static Material s_blitMaterial;

        public string Text
        {
            get => text;
            set { text = value; Bake(); }
        }

        void OnEnable()
        {
            if (bakeOnEnable) Bake();
        }

        void OnValidate()
        {
            textureWidth = Mathf.Max(16, textureWidth);
            textureHeight = Mathf.Max(16, textureHeight);
        }

        void Update()
        {
            // 材质是资产，运行期赋给它的遮罩纹理不会被序列化进 .mat；
            // AssetDatabase 一刷新（存场景、重新导入、域重载）就会丢掉。这里自愈。
            if (_mask == null || _lastAspect <= 0f || string.IsNullOrEmpty(textureProperty)) return;

            foreach (Material m in CachedMaterials)
            {
                if (!m.HasProperty(textureProperty)) continue;
                if (m.GetTexture(textureProperty) != _mask) { ApplyToMaterials(_lastAspect); return; }
            }
        }

        Material[] _cachedMaterials;

        Material[] CachedMaterials => _cachedMaterials ??= ResolveMaterials().ToArray();

        [ContextMenu("Rebake")]
        public void Rebake() => Bake();

        void OnDestroy()
        {
            ReleaseMask();
        }

        void ReleaseMask()
        {
            if (_mask == null) return;
            DestroySafe(_mask);
            _mask = null;
            _lastAspect = 0f;
        }

        static void DestroySafe(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj);
        }

        // ------------------------------------------------------------------
        //  主流程
        // ------------------------------------------------------------------
        public void Bake()
        {
            if (string.IsNullOrEmpty(text)) return;

            var font = ResolveFont();
            if (font == null)
            {
                Debug.LogWarning("[LedTextMaskBaker] 找不到可用字体，跳过烘焙。", this);
                return;
            }

            int outW = Mathf.Clamp(textureWidth, 16, 8192);
            int outH = Mathf.Clamp(ResolveTextureHeight(outW), 16, 8192);

            string[] lines = text.Replace("\r", "").Split('\n');

            // --- pass 1：用探针字号量一下文本块有多大，算出刚好塞进纹理的字号 ---
            const int Probe = 200;
            font.RequestCharactersInTexture(text, Probe, fontStyle);
            float probeW = 1f;
            foreach (string line in lines)
                probeW = Mathf.Max(probeW, MeasureLine(font, line, Probe));

            float probeLineH = Probe * 1.2f;
            float probeBlockH = lines.Length * probeLineH;

            float padPx = padding * outH;
            float availW = Mathf.Max(1f, outW - 2f * padPx);
            float availH = Mathf.Max(1f, outH - 2f * padPx);

            float fit = Mathf.Min(availW / probeW, availH / probeBlockH);
            int size = Mathf.Clamp(Mathf.FloorToInt(Probe * fit), 1, Mathf.Max(1, maxFontSize));

            // --- pass 2：按最终字号重新请求字形，并排出每个字形的位置 ---
            font.RequestCharactersInTexture(text, size, fontStyle);

            Texture atlasSrc = font.material != null ? font.material.mainTexture : null;
            if (atlasSrc == null)
            {
                Debug.LogWarning("[LedTextMaskBaker] 字体没有图集纹理，跳过烘焙。", this);
                return;
            }

            int atlasW = atlasSrc.width;
            int atlasH = atlasSrc.height;
            if (atlasW <= 0 || atlasH <= 0) return;

            LastAtlasInfo = $"{atlasSrc.name} {atlasW}x{atlasH} "
                          + $"{(atlasSrc as Texture2D)?.format.ToString() ?? "n/a"} "
                          + $"isReadable={(atlasSrc as Texture2D)?.isReadable}";

            float lineH = size * 1.2f;
            var glyphs = new List<GlyphQuad>(text.Length);

            for (int li = 0; li < lines.Length; li++)
            {
                string line = lines[li];
                float lineW = MeasureLine(font, line, size);
                float penX = (outW - lineW) * 0.5f;
                // 以纹理中心为基准先随便放，最后按整体包围盒重新居中
                float baseline = -li * lineH;

                foreach (char ch in line)
                {
                    if (ch == '\t') { penX += size * 2f; continue; }
                    if (!font.GetCharacterInfo(ch, out CharacterInfo ci, size, fontStyle)) continue;

                    if (ci.glyphWidth > 0 && ci.glyphHeight > 0)
                    {
                        int dw = Mathf.RoundToInt(ci.maxX - ci.minX);
                        int dh = Mathf.RoundToInt(ci.maxY - ci.minY);   // 目标像素矩形大小

                        if (dw > 0 && dh > 0 && TryGetAtlasRect(in ci, atlasW, atlasH, out int sx, out int sy, out int sw, out int sh))
                        {
                            glyphs.Add(new GlyphQuad
                            {
                                dx = Mathf.RoundToInt(penX + ci.minX),
                                dy = Mathf.RoundToInt(baseline + ci.minY),
                                dw = dw,
                                dh = dh,
                                sx = sx, sy = sy, sw = sw, sh = sh,
                            });
                        }
                    }

                    penX += ci.advance + letterSpacing;
                }
            }

            if (glyphs.Count == 0)
            {
                Debug.LogWarning("[LedTextMaskBaker] 这段文字在字体里一个字形都没解析出来。", this);
                return;
            }

            // --- 按整体包围盒把文字块居中 ---
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (GlyphQuad g in glyphs)
            {
                minX = Mathf.Min(minX, g.dx);
                minY = Mathf.Min(minY, g.dy);
                maxX = Mathf.Max(maxX, g.dx + g.dw);
                maxY = Mathf.Max(maxY, g.dy + g.dh);
            }
            int offX = (outW - (maxX - minX)) / 2 - minX;
            int offY = (outH - (maxY - minY)) / 2 - minY;

            // --- 拉一份字体图集到 CPU（灰度 = 字形覆盖率）---
            byte[] atlas = ReadAtlasCoverage(atlasSrc, out int covStride, out int covW, out int covH, out Texture2D gpuCopy);
            if (atlas == null || covW != atlasW || covH != atlasH)
            {
                DestroySafe(gpuCopy);
                Debug.LogWarning("[LedTextMaskBaker] 字体图集读取失败，跳过烘焙。", this);
                return;
            }
            DestroySafe(gpuCopy);

            // --- 把字形拼到目标纹理上 ---
            var dst = new byte[outW * outH];
            foreach (GlyphQuad g in glyphs)
                Blit(g, offX, offY, atlas, covStride, atlasW, outW, outH, dst);

            var tex = new Texture2D(outW, outH, TextureFormat.R8, false, true)
            {
                name = "LED_Mask_" + Mathf.Abs(text.GetHashCode()).ToString("X8"),
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
            tex.SetPixelData(dst, 0);
            tex.Apply(false, false);

            ReleaseMask();
            _mask = tex;

            ApplyToMaterials(outW / (float)outH);
        }

        // ------------------------------------------------------------------
        //  材质写入
        // ------------------------------------------------------------------
        void ApplyToMaterials(float maskAspect)
        {
            _lastAspect = maskAspect;
            _cachedMaterials = null;

            foreach (Material m in ResolveMaterials())
            {
                if (!string.IsNullOrEmpty(textureProperty) && m.HasProperty(textureProperty))
                    m.SetTexture(textureProperty, _mask);

                if (setAspectProperty && !string.IsNullOrEmpty(aspectProperty) && m.HasProperty(aspectProperty))
                    m.SetFloat(aspectProperty, maskAspect);
            }
        }

        List<Material> ResolveMaterials()
        {
            var mats = new List<Material>();
            if (targetMaterials != null)
                foreach (Material m in targetMaterials)
                    if (m != null) mats.Add(m);

            if (mats.Count == 0)
            {
                var r = GetComponent<Renderer>();
                if (r != null && r.sharedMaterial != null) mats.Add(r.sharedMaterial);
            }

            return mats;
        }

        // ------------------------------------------------------------------
        //  辅助
        // ------------------------------------------------------------------
        int ResolveTextureHeight(int width)
        {
            if (!matchScreenAspect) return textureHeight;

            var screen = GetComponent<LedCurvedScreen>();
            if (screen == null) return textureHeight;

            float aspect = screen.Aspect;
            if (aspect <= 0.01f) return textureHeight;

            return Mathf.Max(16, Mathf.RoundToInt(width / aspect));
        }

        Font ResolveFont()
        {
            if (!string.IsNullOrWhiteSpace(osFontName))
            {
                var f = Font.CreateDynamicFontFromOSFont(osFontName, 32);
                if (f != null && f.material != null && f.material.mainTexture != null)
                    return f;
            }

            // Unity 2022.2 之前叫 Arial.ttf，之后叫 LegacyRuntime.ttf
            var legacy = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (legacy == null) legacy = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return legacy;
        }

        float MeasureLine(Font font, string line, int size)
        {
            float w = 0f;
            foreach (char ch in line)
            {
                if (ch == '\t') { w += size * 2f; continue; }
                if (font.GetCharacterInfo(ch, out CharacterInfo ci, size, fontStyle))
                    w += ci.advance + letterSpacing;
            }
            return w;
        }

        /// <summary>从 CharacterInfo 的四个角推图集矩形。不同 Unity 版本 uv 命名有过对调，取 min/max 最稳。</summary>
        static bool TryGetAtlasRect(in CharacterInfo ci, int atlasW, int atlasH,
                                    out int sx, out int sy, out int sw, out int sh)
        {
            float u0 = Mathf.Min(Mathf.Min(ci.uvBottomLeft.x, ci.uvTopLeft.x),
                                 Mathf.Min(ci.uvBottomRight.x, ci.uvTopRight.x));
            float u1 = Mathf.Max(Mathf.Max(ci.uvBottomLeft.x, ci.uvTopLeft.x),
                                 Mathf.Max(ci.uvBottomRight.x, ci.uvTopRight.x));
            float v0 = Mathf.Min(Mathf.Min(ci.uvBottomLeft.y, ci.uvTopLeft.y),
                                 Mathf.Min(ci.uvBottomRight.y, ci.uvTopRight.y));
            float v1 = Mathf.Max(Mathf.Max(ci.uvBottomLeft.y, ci.uvTopLeft.y),
                                 Mathf.Max(ci.uvBottomRight.y, ci.uvTopRight.y));

            sx = Mathf.Clamp(Mathf.FloorToInt(u0 * atlasW), 0, atlasW - 1);
            // v 小的那一侧在原始像素里仍是低位行，反转在 Blit 里做（图集内容是上下倒的）
            sy = Mathf.Clamp(Mathf.FloorToInt(v0 * atlasH), 0, atlasH - 1);
            sw = Mathf.Clamp(Mathf.CeilToInt((u1 - u0) * atlasW), 0, atlasW - sx);
            sh = Mathf.Clamp(Mathf.CeilToInt((v1 - v0) * atlasH), 0, atlasH - sy);
            return sw > 0 && sh > 0;
        }

        /// <summary>
        /// 把字体图集取成 CPU 字节数组。
        ///
        /// Unity 动态字体的图集是单通道（Alpha8 / R8）且可直接读，所以正常路径是
        /// 一条 memcpy；只有遇到不可读或四通道图集（自定义字体资产）才回退到 GPU blit。
        /// 返回的数组里每 covStride 个字节是一个像素的覆盖率。
        /// </summary>
        public static byte[] ReadAtlasCoverage(Texture src, out int covStride, out int w, out int h, out Texture2D gpuCopy)
        {
            covStride = 1;
            gpuCopy = null;
            w = src.width;
            h = src.height;
            if (w <= 0 || h <= 0) return null;

            // ---- 快路径：单通道且可读，直接拿原始字节 ----
            if (src is Texture2D t2d && t2d.isReadable &&
                (t2d.format == TextureFormat.Alpha8 || t2d.format == TextureFormat.R8))
            {
                try
                {
                    var view = t2d.GetRawTextureData<byte>();
                    var copy = new byte[view.Length];
                    view.CopyTo(copy);
                    if (copy.Length >= w * h) return copy;
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("[LedTextMaskBaker] 直接读取字体图集失败，尝试 GPU 回读：" + e.Message);
                }
            }

            // ---- 慢路径：GPU 回读成 RGBA32，覆盖率放 R 通道 ----
            var mat = GetBlitMaterial();
            if (mat == null)
            {
                Debug.LogWarning("[LedTextMaskBaker] 找不到 Hidden/CartoonRendering/LED/MaskCopyAlpha，无法读取字体图集。");
                return null;
            }

            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            try
            {
                Graphics.Blit(src, rt, mat);

                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                try
                {
                    gpuCopy = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
                    gpuCopy.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                    gpuCopy.Apply(false, false);
                }
                finally
                {
                    RenderTexture.active = prev;
                }
            }
            finally
            {
                RenderTexture.ReleaseTemporary(rt);
            }

            covStride = 4;
            var raw = gpuCopy.GetRawTextureData<byte>();
            var result = new byte[raw.Length];
            raw.CopyTo(result);
            return result;
        }

        static Material GetBlitMaterial()
        {
            if (s_blitMaterial != null) return s_blitMaterial;

            var shader = Shader.Find("Hidden/CartoonRendering/LED/MaskCopyAlpha");
            if (shader == null) return null;

            s_blitMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            return s_blitMaterial;
        }

        static void Blit(GlyphQuad g, int offX, int offY,
                         byte[] atlas, int stride, int atlasW,
                         int outW, int outH, byte[] dst)
        {
            int dx0 = g.dx + offX;
            int dy0 = g.dy + offY;

            for (int y = 0; y < g.dh; y++)
            {
                int dy = dy0 + y;
                if ((uint)dy >= (uint)outH) continue;

                int syLocal = Mathf.Clamp((int)((y + 0.5f) * g.sh / g.dh), 0, g.sh - 1);
                int sy = g.sy + (g.sh - 1 - syLocal);     // 字体图集里字形是上下倒着存的
                int srcRow = sy * atlasW;
                int dstRow = dy * outW;

                for (int x = 0; x < g.dw; x++)
                {
                    int dx = dx0 + x;
                    if ((uint)dx >= (uint)outW) continue;

                    int sx = g.sx + Mathf.Clamp((int)((x + 0.5f) * g.sw / g.dw), 0, g.sw - 1);
                    byte cov = atlas[(srcRow + sx) * stride];

                    int di = dstRow + dx;
                    if (cov > dst[di]) dst[di] = cov;   // 字形重叠时取最大值
                }
            }
        }

        struct GlyphQuad
        {
            public int dx, dy, dw, dh;   // 目标像素矩形（左下角 + 尺寸）
            public int sx, sy, sw, sh;   // 字体图集像素矩形
        }

#if UNITY_EDITOR
        /// <summary>把当前遮罩存成 PNG，方便在 Inspector 外面肉眼检查烘焙结果。</summary>
        [ContextMenu("Bake To PNG (Assets/CartoonRendering/LED)")]
        public void BakeToPng()
        {
            Bake();
            if (_mask == null) return;

            const string dir = "Assets/CartoonRendering/LED";
            if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);

            string file = $"{dir}/Mask_{Mathf.Abs(text.GetHashCode()):X8}.png";
            System.IO.File.WriteAllBytes(file, _mask.EncodeToPNG());
            UnityEditor.AssetDatabase.Refresh();
            Debug.Log($"[LedTextMaskBaker] 遮罩已导出：{file}", this);
        }
#endif
    }
}
