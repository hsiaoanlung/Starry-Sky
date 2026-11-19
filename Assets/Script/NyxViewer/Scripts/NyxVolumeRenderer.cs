// Ray-marched volume rendering of one Nyx grid field, interpolated between snapshots.
// Put this on a Unity Cube (GameObject > 3D Object > Cube). The cube's local [-0.5,0.5]^3
// is the simulation box, so scale the cube to set the on-screen size.
using UnityEngine;

namespace Nyx
{
    [RequireComponent(typeof(MeshRenderer), typeof(MeshFilter))]
    public class NyxVolumeRenderer : MonoBehaviour
    {
        public NyxPlayer player;
        [Tooltip("Shader 'Nyx/Volume' (assigned automatically when the component is added)")]
        public Shader shader;

        [Tooltip("gas_density, dm_density or temperature (see manifest.json)")]
        public string field = "gas_density";

        [Header("Transfer function (in normalised [0,1] field units)")]
        public Gradient colormap;   // defaults to inferno
        [Range(0, 1)] public float low = 0.35f;
        [Range(0, 1)] public float high = 1.0f;
        [Range(0, 5)] public float opacity = 1.0f;
        [Range(0.2f, 4)] public float gamma = 1.5f;
        [Range(0, 5)] public float brightness = 1.5f;
        [Range(16, 512)] public int steps = 160;

        Material _mat;
        Texture2D _lut;
        bool _lutDirty = true;

        void Reset()
        {
            shader = Shader.Find("Nyx/Volume");
            player = NyxColormaps.FindPlayer<NyxPlayer>();
            colormap = NyxColormaps.Inferno();
        }

        void OnValidate() => _lutDirty = true;

        void Start()
        {
            if (shader == null) shader = Shader.Find("Nyx/Volume");
            if (player == null) player = NyxColormaps.FindPlayer<NyxPlayer>();
            if (NyxColormaps.IsUnset(colormap)) colormap = NyxColormaps.Inferno();
            _mat = new Material(shader) { name = "NyxVolume (instance)" };
            GetComponent<MeshRenderer>().sharedMaterial = _mat;
            var col = GetComponent<Collider>();
            if (col) col.enabled = false;
        }

        void Update()
        {
            if (player != null && player.IsReady)
            {
                var fields = player.Manifest.volume_fields;
                for (int i = 0; i < Mathf.Min(9, fields.Length); i++)
                    if (NyxInput.GetKeyDown(KeyCode.Alpha1 + i)) field = fields[i].name;
            }
        }

        void LateUpdate()
        {
            if (_mat == null || player == null || !player.IsReady) return;
            int fi = player.FieldIndex(field);
            if (fi < 0) { Debug.LogWarning($"[Nyx] unknown field '{field}'"); return; }

            if (_lutDirty || _lut == null) { _lut = NyxColormaps.Bake(colormap, _lut); _lutDirty = false; }

            _mat.SetTexture("_VolA", player.GetVolume(player.IndexA, fi));
            _mat.SetTexture("_VolB", player.GetVolume(player.IndexB, fi));
            _mat.SetFloat("_Blend", player.Blend);
            _mat.SetTexture("_LUT", _lut);
            _mat.SetFloat("_Low", low);
            _mat.SetFloat("_High", Mathf.Max(high, low + 1e-3f));
            _mat.SetFloat("_Opacity", opacity);
            _mat.SetFloat("_Gamma", gamma);
            _mat.SetFloat("_Brightness", brightness);
            _mat.SetFloat("_Steps", steps);
        }

        void OnDestroy()
        {
            if (_mat) Destroy(_mat);
            if (_lut) Destroy(_lut);
        }
    }
}
