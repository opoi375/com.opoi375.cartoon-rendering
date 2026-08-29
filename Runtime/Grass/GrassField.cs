using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CartoonRendering
{
    /// <summary>
    /// 卡通交互草场：把已测试的纯逻辑（GrassFieldGenerator 生成布局、
    /// GrassInteractionField 维护脚印场）组装为 GPU 可视化。
    ///
    /// 每帧流程：
    ///   1. 从玩家 Transform Stamp 脚印 → GrassInteractionField（CPU 真相）
    ///   2. Tick 恢复衰减（CPU 真相，测试覆盖）
    ///   3. 用 CommandBuffer.DrawProcedural 把脚印烘焙进交互 RenderTexture
    ///   4. Graphics.DrawMeshInstanced 提交草实例 → CartoonGrass.shader
    ///      （GS 展开草叶，采样交互 RT 弯曲，卡通光照着色）
    ///
    /// 使用方法：在场景中创建空 GameObject，挂此组件，把玩家 Transform、
    /// 草材质和草场参数配置好。先小区域（如 8m×8m）验证效果。
    /// </summary>
    [DisallowMultipleComponent]
    public class GrassField : MonoBehaviour
    {
        [Header("草场")]
        [Tooltip("草场范围（X×Z，米）。原点为组件自身位置")]
        public Vector2 BoundsSize = new Vector2(8f, 8f);

        [Tooltip("每平方米草簇数量")]
        public float DensityPerSquareMeter = 4f;

        [Tooltip("随机种子：固定后布局确定")]
        public int Seed = 42;

        [Tooltip("草簇高度范围（米）")]
        public float MinHeight = 0.4f;
        public float MaxHeight = 0.9f;

        [Tooltip("草簇弯曲范围（0~1）")]
        public float MinBend = 0.2f;
        public float MaxBend = 0.5f;

        [Header("渲染")]
        [Tooltip("使用 CartoonRendering/Grass/CartoonGrass 的材质")]
        public Material GrassMaterial;

        [Tooltip("是否投射阴影")]
        public bool CastShadows = true;

        [Tooltip("是否接收阴影")]
        public bool ReceiveShadows = true;

        [Header("交互")]
        [Tooltip("交互源（玩家）。每帧从其位置记录脚印。" +
                 "未指定时回退到主相机，方便快速验证；正式使用请务必指定玩家 Transform")]
        public Transform InteractionSource;

        [Tooltip("交互半径（米）：草在这个范围内被压弯")]
        public float InteractionRadius = 1.5f;

        [Tooltip("交互强度（0~1）：新脚印的初始强度")]
        public float InteractionStrength = 1f;

        [Tooltip("恢复速度（每秒衰减的强度）")]
        public float RecoverySpeed = 0.1f;

        [Header("交互纹理")]
        [Tooltip("交互纹理分辨率（方形）")]
        public int InteractionTextureSize = 256;

        [Tooltip("交互烘焙材质（使用 CartoonRendering/Grass/GrassInteractionBake）")]
        public Material InteractionBakeMaterial;

        [Header("调试")]
        public bool DrawFieldGizmo = true;

        // ----- 运行时状态 --------------------------------------------------

        private List<GrassBladeData> _blades;
        private GrassInteractionField _field;
        private RenderTexture _interactionTexture;
        private Matrix4x4[] _matrices;
        private MaterialPropertyBlock _propertyBlock;
        private Mesh _bladeBaseMesh;
        private CommandBuffer _bakeCommand;
        private Vector4[] _footprintBuffer;
        private bool _initialized;
        private float _debugTimer;
        private Transform _fallbackSource;
        private bool _warnedNoSource;

        /// <summary>实际生效的交互源：优先 InteractionSource，缺省时回退主相机。</summary>
        private Transform EffectiveSource
        {
            get
            {
                if (InteractionSource != null) return InteractionSource;
                if (_fallbackSource == null && Camera.main != null)
                {
                    _fallbackSource = Camera.main.transform;
                }
                if (_fallbackSource != null && !_warnedNoSource)
                {
                    _warnedNoSource = true;
                    Debug.LogWarning("[GrassField] InteractionSource 未指定（引用可能已断开），" +
                        $"回退到主相机 '{_fallbackSource.name}'。请在 Inspector 中指定玩家 Transform。", this);
                }
                return _fallbackSource;
            }
        }

        /// <summary>当前生成的草布局（只读）。</summary>
        public IReadOnlyList<GrassBladeData> Blades => _blades;

        /// <summary>交互场（CPU 真相，可直接读取）。</summary>
        public GrassInteractionField InteractionField => _field;

        private void OnEnable()
        {
            Initialize();
            Debug.Log($"[GrassField] OnEnable: blades={_blades?.Count ?? -1} mat={GrassMaterial?.name} bakeMat={InteractionBakeMaterial?.name} source={InteractionSource?.name}");
        }

        private void OnDisable()
        {
            ReleaseResources();
        }

        private void Initialize()
        {
            if (_initialized) return;

            var origin = transform.position;
            _blades = GrassFieldGenerator.GenerateUniform(
                BoundsSize, DensityPerSquareMeter, Seed,
                MinHeight, MaxHeight, MinBend, MaxBend);
            _field = new GrassInteractionField();

            CreateInteractionTexture();
            CreateBladeBaseMesh();
            BuildInstanceMatrices(origin);
            AllocateBakeCommand();

            _initialized = true;
        }

        private void Update()
        {
            if (!_initialized) return;

            // 1. 交互源（玩家）Stamp 脚印（合并近距离脚印，避免每帧新增导致列表爆炸）
            var source = EffectiveSource;
            if (source != null)
            {
                _field.Stamp(source.position, InteractionRadius, InteractionStrength,
                    mergeDistance: InteractionRadius * 0.15f);
            }
            // 2. 恢复衰减（CPU 真相）
            _field.Tick(Time.deltaTime, RecoverySpeed);

            // 调试：每 2 秒输出交互状态
            _debugTimer += Time.deltaTime;
            if (_debugTimer >= 2f)
            {
                _debugTimer = 0f;
                float sampleStrength = source != null
                    ? _field.GetBendStrength(source.position)
                    : 0f;
                Debug.Log($"[GrassField] footprints={_field.Footprints.Count} sourceStrength={sampleStrength:F3} sourcePos={(source != null ? source.position.ToString("F2") : "none")}");
            }

            // 3. 烘焙交互场到纹理
            BakeInteractionTexture();

            // 4. 提交实例绘制
            if (GrassMaterial != null && _matrices.Length > 0)
            {
                // DrawMeshInstanced 要求材质启用 instancing（等价于
                // Inspector 的 Enable GPU Instancing 勾选）。运行时强制开启，
                // 避免用户忘记在材质上手动勾选。
                if (!GrassMaterial.enableInstancing)
                {
                    GrassMaterial.enableInstancing = true;
                }

                var bounds = new Bounds(
                    transform.position + new Vector3(BoundsSize.x * 0.5f, MaxHeight * 0.5f, BoundsSize.y * 0.5f),
                    new Vector3(BoundsSize.x + 4f, MaxHeight * 2f, BoundsSize.y + 4f));

                if (_propertyBlock == null) _propertyBlock = new MaterialPropertyBlock();
                _propertyBlock.SetTexture("_InteractionTex", _interactionTexture);
                _propertyBlock.SetVector("_InteractionOrigin", new Vector4(transform.position.x, 0f, transform.position.z, 0f));
                _propertyBlock.SetVector("_InteractionSize", new Vector4(BoundsSize.x, BoundsSize.y, 0f, 0f));
                if (source != null)
                {
                    _propertyBlock.SetVector("_InteractionSource", new Vector4(
                        source.position.x, source.position.y, source.position.z, 0f));
                }

                Graphics.DrawMeshInstanced(
                    _bladeBaseMesh, 0, GrassMaterial, _matrices, _matrices.Length,
                    _propertyBlock, CastShadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                    ReceiveShadows);
            }
        }

        // ------------------------------------------------------------------
        // 交互纹理烘焙
        // ------------------------------------------------------------------
        private void CreateInteractionTexture()
        {
            _interactionTexture = new RenderTexture(InteractionTextureSize, InteractionTextureSize, 0, RenderTextureFormat.R8)
            {
                name = "GrassInteraction",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            _interactionTexture.Create();
        }

        private void AllocateBakeCommand()
        {
            _bakeCommand = new CommandBuffer { name = "GrassInteractionBake" };
            _footprintBuffer = new Vector4[64];
        }

        private void BakeInteractionTexture()
        {
            if (_interactionTexture == null || _bakeCommand == null || InteractionBakeMaterial == null)
                return;

            // 脚印 -> 数组（最多 64 个）。从列表尾部（最新）往前取，
            // 保证玩家当前位置永远在烘焙窗口内；旧足迹会自行衰减消失。
            int count = 0;
            for (int i = _field.Footprints.Count - 1; i >= 0 && count < 64; i--)
            {
                var fp = _field.Footprints[i];
                _footprintBuffer[count++] = new Vector4(fp.Position.x, fp.Position.z, fp.Radius, fp.Strength);
            }

            _bakeCommand.Clear();
            _bakeCommand.SetRenderTarget(_interactionTexture);
            _bakeCommand.ClearRenderTarget(true, true, Color.black);

            InteractionBakeMaterial.SetVector("_GrassInteractionOrigin",
                new Vector4(transform.position.x, 0f, transform.position.z, 0f));
            InteractionBakeMaterial.SetVector("_GrassInteractionSize",
                new Vector4(BoundsSize.x, BoundsSize.y, 0f, 0f));
            InteractionBakeMaterial.SetVectorArray("_GrassFootprints", _footprintBuffer);
            InteractionBakeMaterial.SetInt("_GrassFootprintCount", count);

            _bakeCommand.DrawProcedural(
                Matrix4x4.identity, InteractionBakeMaterial, 0, MeshTopology.Triangles, 3);
            Graphics.ExecuteCommandBuffer(_bakeCommand);
        }

        // ------------------------------------------------------------------
        // 草簇基础网格：由 GrassMeshBuilder.BuildTuft 生成（多片尖叶扇开）。
        // 顶点着色器按高度做风吹/交互弯曲，无需 Geometry Shader。
        // ------------------------------------------------------------------
        private const int GrassBladesPerTuft = 7;
        private const int GrassBladeSegments = 6;
        private const float GrassRootHalfWidth = 0.012f;
        private const float GrassMaxHalfWidth = 0.028f;
        private const float GrassBendAmount = 0.6f;
        // 簇内叶片根部散开半径，避免所有叶片共根挤成一束
        private const float GrassTuftRootSpread = 0.05f;

        private void CreateBladeBaseMesh()
        {
            _bladeBaseMesh = GrassMeshBuilder.BuildTuft(
                GrassBladesPerTuft, GrassBladeSegments,
                GrassRootHalfWidth, GrassMaxHalfWidth, GrassBendAmount,
                GrassTuftRootSpread);
        }

        // ------------------------------------------------------------------
        // 实例矩阵：位置平移 + scale.y 编码草高（GS 从中读取）。
        // ------------------------------------------------------------------
        private void BuildInstanceMatrices(Vector3 origin)
        {
            _matrices = new Matrix4x4[_blades.Count];
            for (int i = 0; i < _blades.Count; i++)
            {
                var blade = _blades[i];
                var pos = origin + new Vector3(blade.Position.x, 0f, blade.Position.z);
                // 高度编码在 scale.y；GS 用 UNITY_MATRIX_M._m11 读取
                _matrices[i] = Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(1f, blade.Height, 1f));
            }
        }

        private void ReleaseResources()
        {
            if (_interactionTexture != null)
            {
                _interactionTexture.Release();
                DestroyImmediate(_interactionTexture);
                _interactionTexture = null;
            }
            if (_bladeBaseMesh != null)
            {
                DestroyImmediate(_bladeBaseMesh);
                _bladeBaseMesh = null;
            }
            _initialized = false;
        }

        private void OnDrawGizmosSelected()
        {
            if (!DrawFieldGizmo) return;
            Gizmos.color = new Color(0.3f, 0.8f, 0.3f, 0.4f);
            Gizmos.DrawCube(transform.position + new Vector3(BoundsSize.x * 0.5f, 0f, BoundsSize.y * 0.5f),
                new Vector3(BoundsSize.x, 0.05f, BoundsSize.y));
        }
    }
}
