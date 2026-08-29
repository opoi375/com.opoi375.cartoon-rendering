// Copyright (c) 2026 CartoonRendering. MIT License.
//
// SDF 帧图的帧号解析。参考知乎专栏 p/702637242：贴图名最后一个下划线分段
// 即帧号，例如 Substance_graph_output_SDF_177 表示第 177 帧。

namespace CartoonRendering.EditorTools
{
    /// <summary>
    /// 从贴图名解析 SDF 帧号（名字最后一个 '_' 分段必须是纯数字）。
    /// </summary>
    public static class SdfFrameParser
    {
        /// <summary>
        /// 尝试解析帧号，失败时不抛异常并返回 false。
        /// </summary>
        public static bool TryParseFrameNumber(string textureName, out int frame)
        {
            frame = 0;
            if (string.IsNullOrEmpty(textureName)) return false;

            int lastUnderscore = textureName.LastIndexOf('_');
            if (lastUnderscore < 0 || lastUnderscore == textureName.Length - 1) return false;

            string suffix = textureName.Substring(lastUnderscore + 1);
            return int.TryParse(suffix, out frame);
        }
    }
}
