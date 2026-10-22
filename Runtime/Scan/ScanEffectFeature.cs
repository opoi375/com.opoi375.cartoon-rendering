// Copyright (c) 2026 CartoonRendering. MIT License.

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CartoonRendering
{
    /// <summary>
    /// 把扫描特效接进渲染器。加在 <c>Renderer Features</c> 里即可，
    /// 或用菜单 <c>Tools &gt; Scan &gt; Setup Scan Effect</c> 一键接好。
    ///
    /// 材质是懒解析的：没指定就按 shader 名在运行时建一个，所以除加 Feature
    /// 之外不需要任何资产配置。
    ///
    /// 与既有后处理的兼容：
    ///   * 默认注入在 <c>BeforeRenderingPostProcessing</c>，也就是在本包的
    ///     UnderwaterPostProcess（同一个事件点）之后入图 —— URP 对同一事件点的
    ///     多个 Feature 按 Renderer 里的顺序执行，扫描特效的辉光因此仍然会
    ///     经过 Bloom / Tonemap，而不是硬糊在最终画面上。
    ///   * 未激活（Volume 关闭 / 强度 0 / 没有触发）时根本不 EnqueuePass，
    ///     不声明深度依赖也不产生任何开销，不会影响普通渲染。
    ///   * 这个 Pass 只做加法与"向扫描色靠拢"，不读 activeColor，所以不会与
    ///     水下后期、体积光、SSIL 抢写同一块缓冲而互相覆盖。
    /// </summary>
    public sealed class ScanEffectFeature : ScriptableRendererFeature
    {
        private const string kShaderName = "CartoonRendering/PostProcessing/ScanEffect";

        [Tooltip("使用 ScanEffect shader 的材质。留空则在运行时自动创建。")]
        public Material material;

        [Tooltip("注入点。默认在不透明与透明物体之后、后处理链之前 —— " +
                 "这样辉光会被管线 Bloom 接手。想让它压在后处理之上就改成 " +
                 "AfterRenderingPostProcessing（但那样就不会被 Bloom/Tonemap 影响）。")]
        public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;

        private static bool s_WarnedNoSettings;
        private ScanEffectPass m_Pass;

        /// <inheritdoc/>
        public override void Create()
        {
            m_Pass = new ScanEffectPass(null)
            {
                renderPassEvent = renderPassEvent
            };
        }

        /// <inheritdoc/>
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            // 三道闸，任何一道不过就不入图：
            //   1) Volume 关闭 / 强度 0
            //   2) 没有正在进行的扫描
            //   3) 材质解析不出来
            var scan = VolumeManager.instance.stack.GetComponent<ScanEffect>();
            if (scan == null)
            {
                // 静默不画是最难查的一类问题：场景里没有 Volume / Volume 的 profile
                // 里没有 Scan Effect 组件时，这里会直接 return，屏幕上什么迹象都没有。
                // 所以只喊一次，并且说清楚怎么补。
                if (!s_WarnedNoSettings && ScanEffectRuntime.active)
                {
                    s_WarnedNoSettings = true;
                    Debug.LogWarning("[ScanEffect] 有扫描在跑，但当前 Volume 栈里没有 Scan Effect 组件，" +
                                     "所以什么都不画。执行 Tools > Scan > Setup Scan Effect 补上，" +
                                     "并确认场景里有一个 Global Volume。");
                }
                return;
            }
            s_WarnedNoSettings = false;

            if (!scan.IsActive())
                return;

            if (!ScanEffectRuntime.active)
                return;

            if (!TryResolveMaterial())
                return;

            if (m_Pass != null)
                m_Pass.renderPassEvent = renderPassEvent;

            renderer.EnqueuePass(m_Pass);
        }

        private bool TryResolveMaterial()
        {
            var shader = Shader.Find(kShaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[ScanEffect] 找不到 shader：{kShaderName}");
                return false;
            }

            // 序列化的材质可能引用旧 shader，跟着改，老场景不会因为 shader 重命名而失效
            if (material != null && material.shader != shader)
                material.shader = shader;

            if (material == null)
                material = new Material(shader) { name = "ScanEffect_Auto" };

            m_Pass.SetMaterial(material);
            return true;
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            // 自动建的材质故意不销毁：每帧复用，随 Feature 对象一起走
        }
    }
}
