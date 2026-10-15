// ============================================================================
// SSILTests.cs
// ----------------------------------------------------------------------------
// SSIL（屏幕空间间接光照）的编辑器冒烟测试：不跑渲染，只锁住"接进管线之前
// 就该成立"的约定 —— Volume 默认关闭、shader 可被找到、pass 数量与
// SSILPass 的索引常量一致。

using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace CartoonRendering.Editor.Tests
{
    public class SSILTests
    {
        [Test]
        public void Volume_Defaults_To_Inactive()
        {
            var volume = ScriptableObject.CreateInstance<SSIL>();
            try
            {
                Assert.That(volume.intensity.value, Is.EqualTo(0f),
                    "intensity 默认必须是 0：VolumeManager 会给没被覆盖的组件返回默认实例，" +
                    "默认非 0 会变成「没放 Volume 也生效」");
                Assert.That(volume.IsActive(), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(volume);
            }
        }

        [Test]
        public void Volume_Active_When_Intensity_Up()
        {
            var volume = ScriptableObject.CreateInstance<SSIL>();
            try
            {
                volume.intensity.Override(1f);
                Assert.That(volume.IsActive(), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(volume);
            }
        }

        [Test]
        public void Shader_Exists_With_Expected_Passes()
        {
            var shader = Shader.Find("CartoonRendering/PostProcessing/SSIL");
            Assert.That(shader, Is.Not.Null, "Shader.Find 找不到 SSIL shader");

            // Trace / BlurH / BlurV / Composite / Debug —— 与 SSILPass 的
            // kPass* 常量一一对应，加 pass 记得同步那边的索引
            Assert.That(shader.passCount, Is.EqualTo(5),
                "SSIL.shader 的 pass 数量变了？记得同步 SSILPass 的 kPass* 索引");
        }

        [Test]
        public void Feature_Creates_Pass()
        {
            var feature = ScriptableObject.CreateInstance<SSILFeature>();
            try
            {
                Assert.DoesNotThrow(feature.Create, "Create() 不该抛异常");
            }
            finally
            {
                Object.DestroyImmediate(feature);
            }
        }
    }
}
