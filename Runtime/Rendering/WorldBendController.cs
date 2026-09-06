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

        [Header("天空/体积云联动")]
        [Tooltip("地面的有效可见距离（米），用于推算天空地平线应下沉的角度。\n调到你场景里地面边缘的大致距离，天空渐变会与弯曲地平线的弧线对齐")]
        [Min(1f)]
        public float skyHorizonDistance = 200f;

        [Tooltip("天空地平线最大下沉量（视空间 Y 单位，0.15 ≈ 8.5°）。\n防止极端曲率下整个天空渐变被拉进“地平线以下”的颜色带")]
        [Range(0f, 0.3f)]
        public float maxSkyDip = 0.15f;

        [Tooltip("云层弯曲强度系数。云层比地面高得多，按同等曲率下垂会快速沉出视野，\n用较小系数（0.3~0.4）让远处云向地平线柔和聚拢")]
        [Range(0f, 1f)]
        public float cloudBendScale = 0.35f;

        [Tooltip("云层最大下垂量（米）。钳制后远处的云停在一个被压低但仍有界的壳层上，\n不会完全沉出天际线")]
        [Min(0f)]
        public float cloudMaxDroop = 300f;

        static readonly int CurvatureID    = Shader.PropertyToID("_WorldBendCurvature");
        static readonly int DeadZoneID     = Shader.PropertyToID("_WorldBendDeadZone");
        static readonly int BendNormalsID  = Shader.PropertyToID("_WorldBendBendNormals");
        static readonly int SkyDipID       = Shader.PropertyToID("_WorldBendSkyDip");
        static readonly int CloudScaleID   = Shader.PropertyToID("_WorldBendCloudScale");
        static readonly int CloudMaxDroopID = Shader.PropertyToID("_WorldBendCloudMaxDroop");

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
            Shader.SetGlobalFloat(SkyDipID, 0f);
            Shader.SetGlobalFloat(CloudScaleID, 0f);
        }

        void Apply()
        {
            Shader.SetGlobalFloat(CurvatureID, curvature);
            Shader.SetGlobalFloat(DeadZoneID, deadZone);
            Shader.SetGlobalFloat(BendNormalsID, bendNormals ? 1f : 0f);
            // 地平线倾角（小角度 tan 近似）：地面边缘在 skyHorizonDistance 处下沉
            // curvature × d²，对应视角 atan(curvature × d) ≈ curvature × d。
            // 钳制上限：极端曲率时若不加 cap，整个渐变会塌进天底颜色带。
            Shader.SetGlobalFloat(SkyDipID, Mathf.Min(curvature * skyHorizonDistance, maxSkyDip));
            // 云层联动：弱化的曲率 + 最大下垂量钳制，避免远处云整体沉出天际
            Shader.SetGlobalFloat(CloudScaleID, curvature > 0f ? cloudBendScale : 0f);
            Shader.SetGlobalFloat(CloudMaxDroopID, cloudMaxDroop);
        }
    }
}
