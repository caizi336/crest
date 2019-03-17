﻿// Crest Ocean System

// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

using UnityEngine;
using UnityEngine.Rendering;

namespace Crest
{
    /// <summary>
    /// A persistent simulation that moves around with a displacement LOD.
    /// </summary>
    public abstract class LodDataMgrPersistent : LodDataMgr
    {
        [SerializeField]
        protected SimSettingsBase _settings;
        public override void UseSettings(SimSettingsBase settings) { _settings = settings; }

        protected readonly int MAX_SIM_STEPS = 4;

        RenderTexture[] _sources;
        Material[,] _renderSimMaterial;

        protected abstract string ShaderSim { get; }

        float _substepDtPrevious = 1f / 60f;

        protected override void Start()
        {
            base.Start();

            var lodCount = OceanRenderer.Instance.CurrentLodCount;
            _renderSimMaterial = new Material[MAX_SIM_STEPS, lodCount];
            var shader = Shader.Find(ShaderSim);
            for (int stepi = 0; stepi < MAX_SIM_STEPS; stepi++)
            {
                for (int i = 0; i < lodCount; i++)
                {
                    _renderSimMaterial[stepi, i] = new Material(shader);
                }
            }
        }

        protected override void InitData()
        {
            base.InitData();

            int resolution = OceanRenderer.Instance.LodDataResolution;
            var desc = new RenderTextureDescriptor(resolution, resolution, TextureFormat, 0);

            _sources = new RenderTexture[OceanRenderer.Instance.CurrentLodCount];
            for (int i = 0; i < _sources.Length; i++)
            {
                _sources[i] = new RenderTexture(desc);
                _sources[i].wrapMode = TextureWrapMode.Clamp;
                _sources[i].antiAliasing = 1;
                _sources[i].filterMode = FilterMode.Bilinear;
                _sources[i].anisoLevel = 0;
                _sources[i].useMipMap = false;
                _sources[i].name = SimName + "_" + i + "_1";
            }
        }

        public void BindSourceData(int lodIdx, int shapeSlot, Material properties, bool paramsOnly, bool usePrevTransform)
        {
            _pwMat._target = properties;

            var rd = usePrevTransform ?
                OceanRenderer.Instance._lods[lodIdx]._renderDataPrevFrame.Validate(BuildCommandBufferBase._lastUpdateFrame - Time.frameCount, this)
                : OceanRenderer.Instance._lods[lodIdx]._renderData.Validate(0, this);

            BindData(lodIdx, shapeSlot, _pwMat, paramsOnly ? Texture2D.blackTexture : (Texture)_sources[lodIdx], true, ref rd);
            _pwMat._target = null;
        }

        public abstract void GetSimSubstepData(float frameDt, out int numSubsteps, out float substepDt);

        public override void BuildCommandBuffer(OceanRenderer ocean, CommandBuffer buf)
        {
            base.BuildCommandBuffer(ocean, buf);

            var lodCount = OceanRenderer.Instance.CurrentLodCount;
            float substepDt;
            int numSubsteps;
            GetSimSubstepData(Time.deltaTime, out numSubsteps, out substepDt);

            for (int stepi = 0; stepi < numSubsteps; stepi++)
            {
                for (var lodIdx = lodCount - 1; lodIdx >= 0; lodIdx--)
                {
                    SwapRTs(ref _sources[lodIdx], ref _targets[lodIdx]);
                }

                for (var lodIdx = lodCount - 1; lodIdx >= 0; lodIdx--)
                {
                    _renderSimMaterial[stepi, lodIdx].SetFloat("_SimDeltaTime", substepDt);
                    _renderSimMaterial[stepi, lodIdx].SetFloat("_SimDeltaTimePrev", _substepDtPrevious);

                    _renderSimMaterial[stepi, lodIdx].SetFloat("_GridSize", OceanRenderer.Instance._lods[lodIdx]._renderData._texelWidth);

                    // compute which lod data we are sampling source data from. if a scale change has happened this can be any lod up or down the chain.
                    // this is only valid on the first update step, after that the scale src/target data are in the right places.
                    var srcDataIdx = lodIdx + ((stepi == 0) ? ScaleDifferencePow2 : 0);

                    // only take transform from previous frame on first substep
                    var usePreviousFrameTransform = stepi == 0;

                    if (srcDataIdx >= 0 && srcDataIdx < lodCount)
                    {
                        // bind data to slot 0 - previous frame data
                        BindSourceData(srcDataIdx, 0, _renderSimMaterial[stepi, lodIdx], false, usePreviousFrameTransform);
                    }
                    else
                    {
                        // no source data - bind params only
                        BindSourceData(lodIdx, 0, _renderSimMaterial[stepi, lodIdx], true, usePreviousFrameTransform);
                    }

                    SetAdditionalSimParams(lodIdx, _renderSimMaterial[stepi, lodIdx]);

                    {
                        var rt = DataTexture(lodIdx);
                        buf.SetRenderTarget(rt, rt.depthBuffer);
                    }

                    buf.DrawMesh(FullScreenQuad(), Matrix4x4.identity, _renderSimMaterial[stepi, lodIdx]);

                    SubmitDraws(lodIdx, buf);
                }

                _substepDtPrevious = substepDt;
            }

            // any post-sim steps. the dyn waves updates the copy sim material, which the anim wave will later use to copy in
            // the dyn waves results.
            for (var lodIdx = lodCount - 1; lodIdx >= 0; lodIdx--)
            {
                BuildCommandBufferInternal(lodIdx);
            }
        }

        protected virtual bool BuildCommandBufferInternal(int lodIdx)
        {
            return true;
        }

        /// <summary>
        /// Set any sim-specific shader params.
        /// </summary>
        protected virtual void SetAdditionalSimParams(int lodIdx, Material simMaterial)
        {
        }

        static Mesh s_fullScreenQuad;
        static Mesh FullScreenQuad()
        {
            if (s_fullScreenQuad != null) return s_fullScreenQuad;

            s_fullScreenQuad = new Mesh();
            s_fullScreenQuad.name = "FullScreenQuad";
            s_fullScreenQuad.vertices = new Vector3[]
            {
                new Vector3(-1f, -1f, 0.1f),
                new Vector3(-1f,  1f, 0.1f),
                new Vector3( 1f,  1f, 0.1f),
                new Vector3( 1f, -1f, 0.1f),
            };
            s_fullScreenQuad.uv = new Vector2[]
            {
                Vector2.up,
                Vector2.zero,
                Vector2.right,
                Vector2.one,
            };

            s_fullScreenQuad.SetIndices(new int[]
            {
                0, 2, 1, 0, 3, 2
            }, MeshTopology.Triangles, 0);

            return s_fullScreenQuad;
        }
    }
}
