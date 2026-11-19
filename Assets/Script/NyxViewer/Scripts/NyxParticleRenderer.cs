// Draws the Nyx dark-matter particles as additive billboards with GPU instancing-free
// procedural drawing (one draw call). Positions are interpolated between snapshots per particle.
// Put this on the same GameObject as NyxVolumeRenderer so both share the box transform.
using UnityEngine;

namespace Nyx
{
    public class NyxParticleRenderer : MonoBehaviour
    {
        public NyxPlayer player;
        [Tooltip("Shader 'Nyx/Particles' (assigned automatically when the component is added)")]
        public Shader shader;

        public bool show = true;
        public Color color = new Color(0.55f, 0.7f, 1.0f, 1.0f);
        [Tooltip("Billboard radius as a fraction of the box size")]
        [Range(0.0005f, 0.02f)] public float pointSize = 0.003f;
        [Range(0, 2)] public float intensity = 0.12f;

        [Header("Colour by speed")]
        public bool colorBySpeed = false;
        public Gradient speedColormap;   // defaults to viridis
        [Tooltip("Speed (km/s) mapped to the top of the colormap; 0 = auto (60% of max)")]
        public float speedMax = 0f;

        Material _mat;
        Texture2D _lut;
        bool _lutDirty = true;

        void Reset()
        {
            shader = Shader.Find("Nyx/Particles");
            player = NyxColormaps.FindPlayer<NyxPlayer>();
            speedColormap = NyxColormaps.Viridis();
        }

        void OnValidate() => _lutDirty = true;

        void Start()
        {
            if (shader == null) shader = Shader.Find("Nyx/Particles");
            if (player == null) player = NyxColormaps.FindPlayer<NyxPlayer>();
            if (NyxColormaps.IsUnset(speedColormap)) speedColormap = NyxColormaps.Viridis();
            if (!SystemInfo.supportsComputeShaders)
                Debug.LogWarning("[Nyx] this platform has no StructuredBuffer support; particles disabled");
            _mat = new Material(shader) { name = "NyxParticles (instance)" };
        }

        void Update()
        {
            if (NyxInput.GetKeyDown(KeyCode.P)) show = !show;
        }

        void LateUpdate()
        {
            if (!show || _mat == null || player == null || !player.IsReady) return;
            var m = player.Manifest;
            if (m.num_particles <= 0) return;

            if (_lutDirty || _lut == null) { _lut = NyxColormaps.Bake(speedColormap, _lut); _lutDirty = false; }

            int a = player.IndexA, b = player.IndexB;
            _mat.SetBuffer("_PosA", player.GetPositions(a));
            _mat.SetBuffer("_PosB", player.GetPositions(b));
            _mat.SetBuffer("_VelA", player.GetVelocities(a));
            _mat.SetBuffer("_VelB", player.GetVelocities(b));
            _mat.SetFloat("_Blend", player.Blend);
            _mat.SetMatrix("_BoxToWorld", transform.localToWorldMatrix);

            float boxWorld = transform.lossyScale.x;
            _mat.SetFloat("_PointSize", pointSize * boxWorld);
            _mat.SetColor("_Color", color);
            _mat.SetFloat("_Intensity", intensity);
            _mat.SetFloat("_ColorBySpeed", colorBySpeed ? 1f : 0f);
            float vmax = speedMax > 0 ? speedMax : 0.6f * Mathf.Max(1f, m.snapshots[b].particle_speed_max);
            _mat.SetFloat("_SpeedMax", vmax);
            _mat.SetTexture("_LUT", _lut);

            var bounds = new Bounds(transform.position, transform.lossyScale * 2f);
            Graphics.DrawProcedural(_mat, bounds, MeshTopology.Triangles, 6 * m.num_particles);
        }

        void OnDestroy()
        {
            if (_mat) Destroy(_mat);
            if (_lut) Destroy(_lut);
        }
    }
}
