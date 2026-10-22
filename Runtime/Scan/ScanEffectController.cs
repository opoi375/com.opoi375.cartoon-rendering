// Copyright (c) 2026 CartoonRendering. MIT License.

using UnityEngine;

namespace CartoonRendering
{
    /// <summary>
    /// 扫描特效的触发器。挂在场景里任意物体上（通常是玩家 / 摄像机 / 管理器）。
    ///
    /// 支持四种触发方式，互不干扰：
    ///   * 手动    —— 代码里调 <see cref="Trigger"/> / Inspector 右键 Trigger
    ///   * 技能    —— 代码里调 <see cref="TriggerSkill"/>（同一套动画，但可以换一套
    ///                外观预设：<see cref="skillRadiusMultiplier"/> 等）
    ///   * 自动演示 —— <see cref="autoDemo"/> 打开后按间隔循环
    ///   * 快捷键  —— <see cref="hotkey"/> 按下即触发（默认 F5），
    ///                <see cref="toggleKey"/> 切换开关（默认 F6）
    ///
    /// 扫描中心：
    ///   <see cref="centerMode"/> = Camera 用当前摄像机；
    ///   Target 用 <see cref="centerTarget"/>（角色 / 载具），缺失时自动回退到摄像机。
    ///
    /// 这个脚本只写 <see cref="ScanEffectRuntime"/>，不碰材质、不碰 Volume —— 外观
    /// 参数在 Volume 上，动画状态在这里，Pass 只读。所以随时删掉它都不会破坏渲染。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("CartoonRendering/Scan Effect Controller")]
    public sealed class ScanEffectController : MonoBehaviour
    {
        [Header("· 中心与形态")]

        [Tooltip("扫描中心取自哪里。Target = 角色（推荐，扫描从脚下往四周推），" +
                 "Camera = 当前摄像机（第一人称 / 演示用）。")]
        public ScanCenter centerMode = ScanCenter.Camera;

        [Tooltip("centerMode = Target 时使用。留空会回退到摄像机。")]
        public Transform centerTarget;

        [Tooltip("扫描形态。Radial = 四周扩散；Frontal = 沿朝向推进（扇形范围由 Volume 的 Sector Angle 控制）。")]
        public ScanPattern pattern = ScanPattern.Radial;

        [Tooltip("Frontal 的推进方向。勾上就用 centerTarget / 摄像机的 forward；" +
                 "取消则用这里的固定世界方向。")]
        public bool useFacingAsForward = true;

        [Tooltip("useFacingAsForward 关闭时使用的固定方向。")]
        public Vector3 fixedForward = Vector3.forward;

        [Header("· 触发")]

        [Tooltip("开场自动触发一次。")]
        public bool triggerOnStart;

        [Tooltip("自动演示：按 autoInterval 循环触发。用于看效果 / 录屏，正式玩法请关掉。")]
        public bool autoDemo;

        [Tooltip("自动演示间隔（秒）。上一次结束后等这么久再触发。")]
        public float autoInterval = 2.5f;

        [Tooltip("快捷键：触发一次。KeyCode.None = 不绑。")]
        public KeyCode hotkey = KeyCode.F5;

        [Tooltip("快捷键：开关效果（改 Volume 里的 enabled）。KeyCode.None = 不绑。")]
        public KeyCode toggleKey = KeyCode.F6;

        [Tooltip("技能触发的半径 / 距离倍率。让技能版比普通扫描更大一圈。")]
        public float skillRadiusMultiplier = 1.6f;

        [Tooltip("技能触发的速度倍率。")]
        public float skillSpeedMultiplier = 1.3f;

        private ScanEffect m_Settings;
        private float m_NextAutoTime;
        private bool m_LocalEnabled = true;
        private float m_SkillTimeLeft;
        private float m_RadiusMul = 1f;
        private float m_SpeedMul = 1f;

        // ------------------------------------------------------------------

        /// <summary>当前是否在扫描中。</summary>
        public bool IsScanning => ScanEffectRuntime.active;

        /// <summary>扫描进度 0..1（做 UI / 音效同步用）。</summary>
        public float Progress => ScanEffectRuntime.progress;

        private void OnEnable()
        {
            m_LocalEnabled = true;
            m_NextAutoTime = Time.time + autoInterval;
            if (triggerOnStart)
                Invoke(nameof(Trigger), 0.15f);
        }

        private void OnDisable()
        {
            // 禁用时把状态清干净：否则会残留半个波前，重新启用后先闪一下
            ScanEffectRuntime.Reset();
        }

        private void Update()
        {
            HandleHotkeys();
            StepAnimation();
            HandleAutoDemo();
        }

        // ------------------------------------------------------------------
        //  触发
        // ------------------------------------------------------------------

        /// <summary>手动触发一次扫描（普通版）。</summary>
        public void Trigger()
        {
            StartScan(1f, 1f);
        }

        /// <summary>技能触发：更大更快的一次扫描。</summary>
        public void TriggerSkill()
        {
            StartScan(skillRadiusMultiplier, skillSpeedMultiplier);
        }

        /// <summary>按名字触发，方便从动画事件 / Timeline / UnityEvent 里调。</summary>
        public void TriggerByName(string kind)
        {
            if (!string.IsNullOrEmpty(kind) &&
                kind.Equals("skill", System.StringComparison.OrdinalIgnoreCase))
                TriggerSkill();
            else
                Trigger();
        }

        /// <summary>立即中止当前扫描并把画面恢复干净。</summary>
        public void Cancel()
        {
            ScanEffectRuntime.Reset();
        }

        /// <summary>切换效果总开关（等价于改 Volume 上的 enabled）。</summary>
        public void ToggleEnabled()
        {
            ToggleEnabled(!m_LocalEnabled);
        }

        private void ToggleEnabled(bool on)
        {
            m_LocalEnabled = on;
            var s = ResolveSettings();
            if (s != null)
                s.enabled.value = on;

            if (!on)
                ScanEffectRuntime.Reset();
        }

        // ------------------------------------------------------------------

        private void StartScan(float radiusMul, float speedMul)
        {
            var s = ResolveSettings();
            if (s == null)
            {
                Debug.LogWarning("[ScanEffect] 场景里找不到带 ScanEffect 的 Volume。" +
                                 "执行 Tools > Scan > Setup Scan Effect，或手动在 Global Volume 上加该组件。");
                return;
            }

            if (!s.enabled.value)
                return;

            m_RadiusMul = Mathf.Max(radiusMul, 0.01f);
            m_SpeedMul  = Mathf.Max(speedMul, 0.01f);
            m_SkillTimeLeft = m_RadiusMul > 1.01f || m_SpeedMul > 1.01f ? 0.5f : 0f;

            var (origin, fwd) = ResolveCenter();

            ScanEffectRuntime.active   = true;
            ScanEffectRuntime.origin   = origin;
            ScanEffectRuntime.forward  = fwd;
            ScanEffectRuntime.front    = 0f;
            ScanEffectRuntime.time     = 0f;
            ScanEffectRuntime.progress = 0f;
            ScanEffectRuntime.fadeOut  = 1f;

            float maxDist = s.pattern.value == ScanPattern.Radial
                ? s.radius.value * m_RadiusMul
                : s.distance.value * m_RadiusMul;
            ScanEffectRuntime.duration = maxDist / Mathf.Max(s.speed.value * m_SpeedMul, 0.01f);

            // 形态以控制器为准（控制器是玩法侧，Volume 是美术侧；玩法要能切形态）
            s.pattern.value = pattern;
        }

        private void StepAnimation()
        {
            if (!ScanEffectRuntime.active)
                return;

            var s = ResolveSettings();
            if (s == null)
            {
                ScanEffectRuntime.Reset();
                return;
            }

            ScanEffectRuntime.time += Time.unscaledDeltaTime;

            float maxDist = s.pattern.value == ScanPattern.Radial
                ? s.radius.value * m_RadiusMul
                : s.distance.value * m_RadiusMul;
            float speed = Mathf.Max(s.speed.value * m_SpeedMul, 0.01f);

            ScanEffectRuntime.front += speed * Time.unscaledDeltaTime;
            ScanEffectRuntime.progress = Mathf.Clamp01(ScanEffectRuntime.front / Mathf.Max(maxDist, 0.01f));

            // 波前走完后再等"恢复时间"，然后整体淡出 —— 否则波前一过立刻消失会硬切
            if (ScanEffectRuntime.front >= maxDist)
            {
                float tail = s.restoreTime.value + 0.35f;
                float over = ScanEffectRuntime.front - maxDist;
                ScanEffectRuntime.fadeOut = Mathf.Clamp01(1f - over / Mathf.Max(tail, 0.01f));
                if (over >= tail)
                    ScanEffectRuntime.Reset();
            }

            if (m_SkillTimeLeft > 0f)
                m_SkillTimeLeft -= Time.unscaledDeltaTime;
            else
            {
                // 技能加成在扫描后半段回落，避免连续释放时倍率一直叠加
                m_RadiusMul = Mathf.MoveTowards(m_RadiusMul, 1f, Time.unscaledDeltaTime * 0.5f);
                m_SpeedMul  = Mathf.MoveTowards(m_SpeedMul, 1f, Time.unscaledDeltaTime * 0.5f);
            }
        }

        private void HandleHotkeys()
        {
            // 走 ScanInput 而不是 Input.GetKeyDown：工程切到新 Input System 后
            // 后者会抛异常，而这里在 Update 最前面，异常会把自动演示与动画推进
            // 一起中断（表现就是"按了没反应、自动演示也不跑"）。
            if (ScanInput.WasPressedThisFrame(hotkey))
                Trigger();

            if (ScanInput.WasPressedThisFrame(toggleKey))
                ToggleEnabled();
        }

        private void HandleAutoDemo()
        {
            if (!autoDemo || ScanEffectRuntime.active || Time.time < m_NextAutoTime)
                return;

            Trigger();
            m_NextAutoTime = Time.time + Mathf.Max(autoInterval, 0.5f);
        }

        // ------------------------------------------------------------------

        private (Vector3 origin, Vector3 forward) ResolveCenter()
        {
            Transform src = null;
            if (centerMode == ScanCenter.Target && centerTarget != null)
                src = centerTarget;
            else if (Camera.main != null)
                src = Camera.main.transform;

            if (src == null)
            {
                // 最后兜底：用本脚本所在的 transform
                src = transform;
            }

            Vector3 fwd = useFacingAsForward ? src.forward : fixedForward.normalized;
            if (fwd.sqrMagnitude < 1e-6f)
                fwd = Vector3.forward;

            return (src.position, fwd.normalized);
        }

        private ScanEffect ResolveSettings()
        {
            // 缓存的组件可能因为切换 Volume 而失效，用 UnityEngine.Object 判空即可
            if (m_Settings != null)
                return m_Settings;

            var stack = UnityEngine.Rendering.VolumeManager.instance.stack;
            m_Settings = stack != null ? stack.GetComponent<ScanEffect>() : null;
            return m_Settings;
        }
    }
}
