using System.Collections.Generic;
using UnityEngine;

namespace CartoonRendering
{
    /// <summary>
    /// 草地交互场：记录脚印（位置 + 强度 + 半径），随时间按恢复速度衰减。
    /// 查询某位置草时返回该位置受到的最强弯曲强度。
    /// 有状态、确定性，供 Geometry Shader 每帧读取执行可视化，可直接在 EditMode 测试。
    /// </summary>
    public class GrassInteractionField
    {
        /// <summary>单个脚印。</summary>
        public struct Footprint
        {
            public Vector3 Position;
            public float Strength;
            public float Radius;
        }

        private readonly List<Footprint> _footprints = new();

        /// <summary>当前所有脚印（只读遍历用）。</summary>
        public IReadOnlyList<Footprint> Footprints => _footprints;

        /// <summary>脚印数量上限：超出时丢弃最旧的脚印，防止每帧盖章导致无限增长。</summary>
        public const int MaxFootprints = 256;

        /// <summary>
        /// 记录一个脚印。玩家行走时每步调用一次，新脚印位置的草随即被压下。
        /// mergeDistance &gt; 0 时：若已存在距离小于 mergeDistance 的脚印，
        /// 则刷新该脚印（更新位置/强度并移到列表末尾作为"最新"），不再新增。
        /// 默认 0 = 保持原行为（每次调用都新增），兼容既有测试。
        /// </summary>
        public void Stamp(Vector3 position, float radius, float strength, float mergeDistance = 0f)
        {
            if (mergeDistance > 0f)
            {
                float mergeSqr = mergeDistance * mergeDistance;
                for (int i = _footprints.Count - 1; i >= 0; i--)
                {
                    var fp = _footprints[i];
                    if ((fp.Position - position).sqrMagnitude > mergeSqr) continue;

                    fp.Position = position;
                    fp.Radius = radius;
                    fp.Strength = Mathf.Max(fp.Strength, strength);
                    _footprints.RemoveAt(i);
                    _footprints.Add(fp); // 移到末尾：尾部 = 最新
                    return;
                }
            }

            _footprints.Add(new Footprint { Position = position, Strength = strength, Radius = radius });

            while (_footprints.Count > MaxFootprints)
            {
                _footprints.RemoveAt(0);
            }
        }

        /// <summary>
        /// 推进一个时间步：所有脚印强度按恢复速度线性衰减，衰减到零的脚印被移除。
        /// </summary>
        public void Tick(float deltaTime, float recoverySpeed)
        {
            for (int i = _footprints.Count - 1; i >= 0; i--)
            {
                var fp = _footprints[i];
                fp.Strength = Mathf.Max(0f, fp.Strength - recoverySpeed * deltaTime);
                if (fp.Strength <= 0f)
                {
                    _footprints.RemoveAt(i);
                }
                else
                {
                    _footprints[i] = fp;
                }
            }
        }

        /// <summary>
        /// 查询某位置草在当前交互场下的弯曲强度（取覆盖该位置的最强脚印）。
        /// 同一时刻场包含所有脚印，每个脚印对覆盖范围内的草提供 1 - d/R 的衰减强度。
        /// </summary>
        public float GetBendStrength(Vector3 grassPosition)
        {
            float best = 0f;
            foreach (var fp in _footprints)
            {
                if (fp.Strength <= 0f || fp.Radius <= 0f) continue;
                float dist = Vector3.Distance(grassPosition, fp.Position);
                if (dist >= fp.Radius) continue;
                float falloff = 1f - dist / fp.Radius;
                best = Mathf.Max(best, fp.Strength * falloff);
            }
            return best;
        }
    }
}
