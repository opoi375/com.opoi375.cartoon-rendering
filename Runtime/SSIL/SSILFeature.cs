// ============================================================================
// SSILFeature.cs
// ----------------------------------------------------------------------------
// 把 SSIL（屏幕空间间接光照 + 可见性位掩码）挂到 URP 渲染器上。
//
// 加进来的方式：菜单 "Tools/SSIL/Setup In Renderer"（一键写进
// CartoonRP_Renderer.asset），或手动在 Renderer Features 里 Add
// "Screen Space Indirect Light"。
//
// 材质是懒加载的：没指定材质时会按 shader 名在运行时建一个，所以接进来就
// 能用，不需要额外建资产。
//
// 注入点默认 BeforeRenderingTransparents：不透明物体与天空盒画完、深度图 /
// 法线图 / 不透明色拷贝都就绪，此时把 AO + 间接光乘进场景色；之后绘制的
// 透明物体（水面、玻璃）不会被二次压暗。
// ============================================================================

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CartoonRendering
{
    public sealed class SSILFeature : ScriptableRendererFeature
    {
        /// <summary>追踪（及模糊）的分辨率除数。</summary>
        public enum ResolutionDivisor
        {
            Full = 1,
            Half = 2,
            Quarter = 4,
        }

        private const string kShaderName = "CartoonRendering/PostProcessing/SSIL";

        [Tooltip("使用 SSIL shader 的材质。留空的话运行时自动建一个。")]
        public Material material;

        [Tooltip("注入点。默认在透明物体之前：此时不透明物体与天空盒都已经画完，" +
                 "深度图 / 法线图 / 不透明色拷贝都就绪。")]
        public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;

        [Tooltip("追踪的分辨率。Half 是质量与开销的平衡点；结果会模糊 + 上采样，" +
                 "Full 只在需要极干净的遮挡边缘时用。")]
        public ResolutionDivisor resolution = ResolutionDivisor.Half;

        private SSILPass m_Pass;

        /// <inheritdoc/>
        public override void Create()
        {
            m_Pass = new SSILPass(null);
        }

        /// <inheritdoc/>
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!TryResolveMaterial())
                return;

            m_Pass.renderPassEvent = renderPassEvent;
            m_Pass.SetResolutionDivisor((int)resolution);
            renderer.EnqueuePass(m_Pass);
        }

        private bool TryResolveMaterial()
        {
            var shader = Shader.Find(kShaderName);
            if (shader == null)
                return false;

            // 已存在的材质可能指向旧 shader，同步一下，让老配置在改 shader 后不炸
            if (material != null && material.shader != shader)
                material.shader = shader;

            if (material == null)
                material = new Material(shader) { name = "SSIL_Auto" };

            m_Pass.SetMaterial(material);
            return true;
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            // 自动建的材质很小，跟 feature 对象一起回收，不额外释放
        }
    }
}
