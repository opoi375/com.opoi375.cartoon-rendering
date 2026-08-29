// Copyright (c) 2026 CartoonRendering. MIT License.
//
// GPU SDF 生成工具窗口，菜单：Tools > SDF > SDF Generator。
// 参考知乎专栏 p/702637242《写个GPU上运行的SDF生成器》：
//   页签 1「遮罩 → SDF」：把黑白遮罩图烘焙成归一化 SDF 贴图（0.5 为分界线）；
//   页签 2「多帧 → 渐变」：把一组名字以 "_帧号" 结尾的 SDF 贴图（如 xxx_SDF_177）
//                         插值合成渐变贴图，用于溶解/燃烧/生长类效果。

using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CartoonRendering.EditorTools
{
    /// <summary>
    /// SDF 生成工具编辑器窗口。
    /// </summary>
    public class SdfGeneratorWindow : EditorWindow
    {
        private const string OutputFolder = "Assets/CartoonRendering/Textures/SDF";
        private static readonly string[] TabNames = { "遮罩 → SDF", "多帧 → 渐变" };

        private ComputeShader _compute;
        private int _tab;

        // 页签 1：遮罩 → SDF
        private Texture2D _mask;
        private float _scaleDown = 512f;
        private string _sdfOutputName = "Face_SDF";
        private RenderTexture _sdfPreview;

        // 页签 2：多帧 → 渐变
        private List<Texture2D> _frames = new List<Texture2D>();
        private Vector2 _frameListScroll;
        private string _gradientOutputName = "SDF_Gradient";
        private RenderTexture _gradientPreview;

        [MenuItem("Tools/SDF/SDF Generator")]
        public static void Open()
        {
            var window = GetWindow<SdfGeneratorWindow>("SDF Generator");
            window.minSize = new Vector2(360, 420);
            window.Show();
        }

        private void OnEnable()
        {
            _compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(SdfComputeBaker.ComputeShaderPath);
            if (_compute == null)
            {
                string guid = AssetDatabase.FindAssets("t:ComputeShader SdfGenerator").FirstOrDefault();
                if (guid != null)
                    _compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(AssetDatabase.GUIDToAssetPath(guid));
            }
        }

        private void OnDisable()
        {
            ReleasePreview(ref _sdfPreview);
            ReleasePreview(ref _gradientPreview);
        }

        private void OnGUI()
        {
            if (_compute == null)
            {
                EditorGUILayout.HelpBox("未找到 SdfGenerator.compute，请确认文件位于 " +
                                        SdfComputeBaker.ComputeShaderPath, MessageType.Error);
                return;
            }

            _tab = GUILayout.Toolbar(_tab, TabNames);
            EditorGUILayout.Space();

            if (_tab == 0) DrawMaskToSdfTab();
            else DrawGradientTab();
        }

        // ---------------- 页签 1：遮罩 → SDF ----------------

        private void DrawMaskToSdfTab()
        {
            EditorGUILayout.HelpBox("白色(>0.5)为内部/障碍，黑色为背景。输出归一化到 [0,1]，" +
                                    "0.5 为分界线，外部 > 0.5，内部 < 0.5。", MessageType.Info);

            _mask = (Texture2D)EditorGUILayout.ObjectField("黑白遮罩图", _mask, typeof(Texture2D), false);
            _scaleDown = EditorGUILayout.Slider("距离缩放 (ScaleDown)", _scaleDown, 1f, 4096f);
            _sdfOutputName = EditorGUILayout.TextField("输出文件名", _sdfOutputName);

            using (new EditorGUI.DisabledScope(_mask == null))
            {
                if (GUILayout.Button("生成 SDF"))
                    BakeMaskToSdf();
            }

            DrawPreview(_sdfPreview);
        }

        private void BakeMaskToSdf()
        {
            RenderTexture previous = _sdfPreview;
            _sdfPreview = SdfComputeBaker.BakeSdfToTexture(
                _compute, _mask, _mask.width, _mask.height, _scaleDown);
            ReleasePreview(ref previous);

            SaveRenderTexture(_sdfPreview, _sdfOutputName);
        }

        // ---------------- 页签 2：多帧 → 渐变 ----------------

        private void DrawGradientTab()
        {
            EditorGUILayout.HelpBox("把一组 SDF 帧图（名字以 \"_帧号\" 结尾，如 xxx_SDF_177）" +
                                    "插值合成为渐变贴图：每个像素记录其从第几个明度层级进入内部。",
                MessageType.Info);

            _frameListScroll = EditorGUILayout.BeginScrollView(_frameListScroll, GUILayout.MaxHeight(180));
            int removeAt = -1;
            for (int i = 0; i < _frames.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                _frames[i] = (Texture2D)EditorGUILayout.ObjectField(_frames[i], typeof(Texture2D), false);

                string frameLabel = "--";
                if (_frames[i] != null &&
                    SdfFrameParser.TryParseFrameNumber(_frames[i].name, out int frame))
                {
                    frameLabel = frame.ToString();
                }
                GUILayout.Label($"帧: {frameLabel}", GUILayout.Width(70));

                if (GUILayout.Button("×", GUILayout.Width(24))) removeAt = i;
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("添加帧")) _frames.Add(null);
            if (GUILayout.Button("清空")) _frames.Clear();
            EditorGUILayout.EndHorizontal();
            if (removeAt >= 0) _frames.RemoveAt(removeAt);

            _gradientOutputName = EditorGUILayout.TextField("输出文件名", _gradientOutputName);

            int validCount = _frames.Count(t => t != null &&
                SdfFrameParser.TryParseFrameNumber(t.name, out _));
            using (new EditorGUI.DisabledScope(validCount < 2))
            {
                if (GUILayout.Button($"合成渐变贴图（有效帧: {validCount}）"))
                    ComposeGradient();
            }
            if (validCount < 2)
                EditorGUILayout.HelpBox("至少需要两张带合法帧号后缀的 SDF 贴图。", MessageType.Warning);

            DrawPreview(_gradientPreview);
        }

        private void ComposeGradient()
        {
            var frames = new Dictionary<int, Texture>();
            foreach (Texture2D tex in _frames.Where(t => t != null))
            {
                if (SdfFrameParser.TryParseFrameNumber(tex.name, out int frame))
                    frames[frame] = tex;
            }

            Texture first = frames.Values.First();
            RenderTexture previous = _gradientPreview;
            _gradientPreview = SdfComputeBaker.ComposeGradientToTexture(
                _compute, frames, first.width, first.height);
            ReleasePreview(ref previous);

            SaveRenderTexture(_gradientPreview, _gradientOutputName);
        }

        // ---------------- 公共 ----------------

        private static void DrawPreview(RenderTexture rt)
        {
            if (rt == null) return;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("预览", EditorStyles.boldLabel);
            Rect rect = GUILayoutUtility.GetAspectRect(1f, GUILayout.MaxHeight(220));
            EditorGUI.DrawPreviewTexture(rect, rt, null, ScaleMode.ScaleToFit);
        }

        private static void SaveRenderTexture(RenderTexture rt, string outputName)
        {
            if (string.IsNullOrWhiteSpace(outputName)) outputName = "SDF_Output";

            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            try
            {
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();

                Directory.CreateDirectory(OutputFolder);
                string path = $"{OutputFolder}/{outputName}.png";
                File.WriteAllBytes(path, tex.EncodeToPNG());
                AssetDatabase.ImportAsset(path);

                // SDF 数据贴图：关闭 sRGB，避免 gamma 干扰
                if (AssetImporter.GetAtPath(path) is TextureImporter importer)
                {
                    importer.sRGBTexture = false;
                    importer.mipmapEnabled = false;
                    importer.SaveAndReimport();
                }
                Debug.Log($"[SDF Generator] 已保存: {path}");
            }
            finally
            {
                RenderTexture.active = previous;
                DestroyImmediate(tex);
            }
        }

        private static void ReleasePreview(ref RenderTexture rt)
        {
            if (rt == null) return;
            rt.Release();
            DestroyImmediate(rt);
            rt = null;
        }
    }
}
