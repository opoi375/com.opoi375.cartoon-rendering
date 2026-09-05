using UnityEngine;

namespace Opoi375.CartoonRendering
{
    /// <summary>
    /// 动森式"小星球"世界弯曲的全局控制器。
    /// 把本组件挂在场景任意激活物体上（建议挂在主相机或场景管理物体），
    /// 每帧把曲率参数推送到全局 Shader 变量；curvature = 0 时完全不弯曲。
    ///
    /// 曲率与等效星球半径的关系：curvature = 1 / (2 × 星球半径)
    ///   半径  500m → 0.0010（夸张，动森感）
    ///   半径 1250m → 0.0004（适中，默认）
    ///   半径 5000m → 0.0001（微妙）
    ///
    /// 注意：这是纯视觉效果。碰撞、寻路、物理仍按未弯曲的世界运行；
    /// Unity Terrain 与 URP 内置 Lit 材质不受控（需换用包内 shader）。
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Cartoon Rendering/World Bend Controller")]
    public class WorldBendController : MonoBehaviour
    {
        [Tooltip("世界曲率 = 1/(2×星球半径)。0 = 关闭弯曲")]
        [Range(0f, 0.020f)]
        public float curvature = 0.0004f;

        [Tooltip("相机附近保持平直的半径（米），防止脚下地面穿帮")]
        [Min(0f)]
        public float deadZone = 10f;

        [Tooltip("是否同步倾斜法线，让光照跟随曲面（近处无感、远处更自然）")]
        public bool bendNormals = true;

        static readonly int CurvatureID   = Shader.PropertyToID("_WorldBendCurvature");
        static readonly int DeadZoneID    = Shader.PropertyToID("_WorldBendDeadZone");
        static readonly int BendNormalsID = Shader.PropertyToID("_WorldBendBendNormals");

        void OnEnable()  => Apply();
        void OnValidate() => Apply();
        void Update()
        {
            // ExecuteAlways 下编辑模式 Update 也会跑，保证 Scene 视图实时预览
            Apply();
        }

        void OnDisable()
        {
            Shader.SetGlobalFloat(CurvatureID, 0f);
        }

        void Apply()
        {
            Shader.SetGlobalFloat(CurvatureID, curvature);
            Shader.SetGlobalFloat(DeadZoneID, deadZone);
            Shader.SetGlobalFloat(BendNormalsID, bendNormals ? 1f : 0f);
        }
    }
}
