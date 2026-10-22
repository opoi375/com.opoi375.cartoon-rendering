// Copyright (c) 2026 CartoonRendering. MIT License.

using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace CartoonRendering
{
    /// <summary>扫描形态。</summary>
    public enum ScanPattern
    {
        /// <summary>以扫描中心为球心，向四周扩散。</summary>
        Radial = 0,
        /// <summary>沿扫描方向推进（平面 + 可选扇形范围）。</summary>
        Frontal = 1,
        /// <summary>体素风格：世界量化成立方格，波前逐块点亮"立体方格"。</summary>
        Voxel = 2,
    }

    /// <summary>扫描中心取自哪里。</summary>
    public enum ScanCenter
    {
        /// <summary>当前摄像机位置。</summary>
        Camera = 0,
        /// <summary>指定 Transform（角色 / 载具）。缺失时回退到摄像机。</summary>
        Target = 1,
    }

    /// <summary>质量档位。只影响采样与附加效果的开关，不影响接口。</summary>
    public enum ScanQuality
    {
        Low = 0,
        Medium = 1,
        High = 2,
    }

    /// <summary>恢复曲线。</summary>
    public enum ScanRestoreCurve
    {
        Linear = 0,
        Smooth = 1,
        Quadratic = 2,
    }

    // ------------------------------------------------------------------
    //  Volume 参数包装（Volume 框架没有内置枚举参数）
    // ------------------------------------------------------------------

    /// <summary>枚举型 Volume 参数。</summary>
    [Serializable]
    public sealed class ScanEnumParameter<T> : VolumeParameter<T> where T : Enum
    {
        public ScanEnumParameter(T value, bool overrideState = false) : base(value, overrideState) { }
    }

    /// <summary>
    /// 扫描特效的外观参数（Volume）。
    ///
    /// 动画状态（当前半径、扫描中心、进度）不在这里 —— Volume 资产是共享的，
    /// 每帧写它会把资产标脏。那部分放在 <see cref="ScanEffectRuntime"/>，
    /// 由 <see cref="ScanEffectController"/> 推进。
    ///
    /// <see cref="intensity"/> 为 0 时 <see cref="IsActive"/> 返回 false，
    /// Feature 根本不会把 Pass 挂进渲染图 —— 关闭时是零开销，不是"跑一遍再乘 0"。
    /// </summary>
    [Serializable]
    [VolumeComponentMenu("CartoonRendering/Scan Effect")]
    public sealed class ScanEffect : VolumeComponent, IPostProcessComponent
    {
        // ---------------------------------------------------------- 总控

        [Tooltip("总强度，0 = 关闭。为 0 时通道根本不入图（零开销）。")]
        public ClampedFloatParameter intensity = new ClampedFloatParameter(1f, 0f, 4f, false);

        [Tooltip("主开关。关掉后即使有触发也不渲染，用于临时排查「是不是这个效果搞坏的」。")]
        public BoolParameter enabled = new BoolParameter(true, false);

        [Tooltip("扫描形态：Radial = 以中心向四周扩散；Frontal = 沿朝向推进（可加扇形范围）。")]
        public ScanEnumParameter<ScanPattern> pattern = new ScanEnumParameter<ScanPattern>(ScanPattern.Radial, false);

        [Tooltip("质量档位。Low 关掉噪波扰动与边缘描边；High 额外加一层噪波并启用深度描边。")]
        public ScanEnumParameter<ScanQuality> quality = new ScanEnumParameter<ScanQuality>(ScanQuality.Medium, false);

        // ---------------------------------------------------------- 范围

        [Tooltip("最大扫描半径（米）。Radial 用；Frontal 同时用它做距离衰减基准。")]
        public MinFloatParameter radius = new MinFloatParameter(40f, 0.1f, false);

        [Tooltip("最大推进距离（米）。Frontal 用；Radial 忽略。")]
        public MinFloatParameter distance = new MinFloatParameter(80f, 0.1f, false);

        [Tooltip("扫描速度（米/秒）。决定波前推进快慢。")]
        public MinFloatParameter speed = new MinFloatParameter(45f, 0.01f, false);

        [Tooltip("扇形半角（度）。360 = 整圈；Frontal 常用 30~70 做「视野前方扫描」。")]
        public ClampedFloatParameter sectorAngle = new ClampedFloatParameter(360f, 1f, 360f, false);

        // ---------------------------------------------------------- 外观

        [Tooltip("波前厚度（米）。薄 = 锐利一道光；厚 = 一整段发光带。")]
        public MinFloatParameter lineWidth = new MinFloatParameter(1.2f, 0.01f, false);

        [Tooltip("扫描主色（网格 / 点阵 / 扫描区染色）。")]
        public ColorParameter color = new ColorParameter(new Color(0.25f, 0.95f, 1f, 1f), false);

        [Tooltip("边缘与波前强调色（波前、描边、辉光都取这个）。")]
        public ColorParameter edgeColor = new ColorParameter(new Color(0.6f, 1f, 1f, 1f), false);

        [Tooltip("边缘高亮强度（深度不连续处的描边）。0 = 关闭。")]
        public ClampedFloatParameter edgeIntensity = new ClampedFloatParameter(1f, 0f, 4f, false);

        [Tooltip("辉光强度。会被管线 Bloom 接手，所以别一下子拉太高。")]
        public ClampedFloatParameter glowIntensity = new ClampedFloatParameter(1.2f, 0f, 8f, false);

        [Tooltip("拖尾长度（米）。波前后面的余辉衰减距离，0 = 没有拖尾。")]
        public MinFloatParameter trailLength = new MinFloatParameter(6f, 0f, false);

        [Tooltip("网格密度（每米格数）。0.2 ≈ 5 米一格。")]
        public MinFloatParameter gridDensity = new MinFloatParameter(0.2f, 0f, false);

        [Tooltip("点阵密度（每米点数）。0.5 ≈ 2 米一个点。")]
        public MinFloatParameter dotDensity = new MinFloatParameter(0.5f, 0f, false);

        [Tooltip("扫描区细节强度（网格 / 点阵 / 扫描线总强度）。0 = 只有波前和辉光。")]
        public ClampedFloatParameter detailIntensity = new ClampedFloatParameter(1f, 0f, 3f, false);

        // ---------------------------------------------------------- 行为

        [Tooltip("恢复时间（秒）。波前经过后，被扫区域用多久退回原材质。0.1 = 一闪而过，3 = 停留很久。")]
        public MinFloatParameter restoreTime = new MinFloatParameter(0.8f, 0.01f, false);

        [Tooltip("恢复曲线。Linear 匀速退；Smooth 两头缓；Quadratic 停留久、退得快。")]
        public ScanEnumParameter<ScanRestoreCurve> restoreCurve =
            new ScanEnumParameter<ScanRestoreCurve>(ScanRestoreCurve.Smooth, false);

        [Tooltip("噪波扰动。把波前半径按方向抖动，避免一个完美的圆规圆。0 = 完美环形。")]
        public ClampedFloatParameter noise = new ClampedFloatParameter(0.15f, 0f, 1f, false);

        [Tooltip("诊断开关：勾上后整个通道强制输出纯红。用来判定这条 draw 到底有没有写进画面。")]
        public BoolParameter diagnosticSolid = new BoolParameter(false, false);

        [Header("· 体素风格")]

        [Tooltip("体素边长（米）。大于 0 就切到「立体方格」模式：把世界量化成立方格，波前逐块点亮。 0 = 关闭（用基础线框）。")]
        public MinFloatParameter voxelSize = new MinFloatParameter(0f, 0f, false);

        [Tooltip("体素内部填充强度。 0 = 只有线框（空心格）； 1 = 实心块（每块带哈希明暗）。")]
        public ClampedFloatParameter voxelFill = new ClampedFloatParameter(0.45f, 0f, 1f, false);

        [Tooltip("波前揭开宽度（米）。越小，格子被点亮越崩脆；越大越柔和。")]
        public MinFloatParameter voxelReveal = new MinFloatParameter(3f, 0.01f, false);

        [Header("· 行为")]

        [Tooltip("距离衰减指数。0 = 不衰减；越大远处越弱（读作信号衰减）。")]
        public ClampedFloatParameter attenuation = new ClampedFloatParameter(1f, 0f, 4f, false);

        // ---------------------------------------------------------- 接口

        /// <summary>总开关 + 强度都就位才算激活。为 false 时 Feature 不 EnqueuePass。</summary>
        public bool IsActive() => enabled.value && intensity.value > 0f;
    }
}
