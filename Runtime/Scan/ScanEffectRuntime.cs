// Copyright (c) 2026 CartoonRendering. MIT License.

using UnityEngine;

namespace CartoonRendering
{
    /// <summary>
    /// 扫描特效的运行时动画状态。
    ///
    /// 为什么单独放一个静态类而不是塞进 Volume：
    ///   Volume 资产是共享的 ScriptableObject，每帧写就会把资产标脏、进版本控制，
    ///   而且多相机之间会互相干扰。外观参数归 Volume，动画状态归这里 ——
    ///   职责分开后，Pass 只读、Controller 只写。
    ///
    /// 这个状态是【全局】的：一次触发对所有相机同时生效。这是雷达 / 扫描类
    /// 效果的常规做法（分屏多相机各自扫描反而是错的）。
    /// </summary>
    public static class ScanEffectRuntime
    {
        /// <summary>当前是否有扫描在跑。Pass 会再乘 Volume 的 IsActive。</summary>
        public static bool active;

        /// <summary>扫描中心（世界坐标）。</summary>
        public static Vector3 origin;

        /// <summary>扫描方向（世界坐标，已归一化）。Frontal 用。</summary>
        public static Vector3 forward = Vector3.forward;

        /// <summary>当前波前距离（米）。Radial 是与中心的距离，Frontal 是沿方向的投影距离。</summary>
        public static float front;

        /// <summary>整体进度 0..1（仅为调试 / UI 提供，着色器不直接用）。</summary>
        public static float progress;

        /// <summary>开始到现在的时间（秒），驱动网格滚动与噪波演化。</summary>
        public static float time;

        /// <summary>本次扫描的总时长（秒），= 最大距离 / 速度。</summary>
        public static float duration;

        /// <summary>结束时整体淡出 1 → 0，避免最后一帧硬切。</summary>
        public static float fadeOut = 1f;

        /// <summary>清零。关闭效果 / 切场景时调用，保证不会残留一个"半个波前"。</summary>
        public static void Reset()
        {
            active   = false;
            origin   = Vector3.zero;
            forward  = Vector3.forward;
            front    = 0f;
            progress = 0f;
            time     = 0f;
            duration = 0f;
            fadeOut  = 1f;
        }
    }
}
