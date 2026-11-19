// Loads a converted Nyx dataset from StreamingAssets and drives time playback.
// Renderers (NyxVolumeRenderer, NyxParticleRenderer) read IndexA / IndexB / Blend from here.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Nyx
{
    public class NyxPlayer : MonoBehaviour
    {
        [Tooltip("Folder under Assets/StreamingAssets that contains manifest.json")]
        public string datasetFolder = "NyxLyA64";

        [Tooltip("Load every snapshot at start (avoids hitches during playback; ~100 MB for 64^3)")]
        public bool preloadAll = true;

        [Header("Playback")]
        public bool playing = true;
        [Tooltip("Snapshots per second")]
        public float speed = 0.5f;
        public bool loop = true;
        [Tooltip("Current position: 0 = first snapshot, N-1 = last. Fractional values interpolate.")]
        public float position = 0f;

        [Header("Overlay")]
        public bool showOverlay = true;

        public NyxManifest Manifest { get; private set; }
        public bool IsReady => Manifest != null;
        public int SnapshotCount => Manifest?.snapshots.Length ?? 0;
        public int IndexA { get; private set; }
        public int IndexB { get; private set; }
        public float Blend { get; private set; }

        readonly Dictionary<(int, int), Texture3D> _volumes = new Dictionary<(int, int), Texture3D>();
        readonly Dictionary<int, ComputeBuffer> _positions = new Dictionary<int, ComputeBuffer>();
        readonly Dictionary<int, ComputeBuffer> _velocities = new Dictionary<int, ComputeBuffer>();

        string Root => Path.Combine(Application.streamingAssetsPath, datasetFolder);

        void Awake()
        {
            string path = Path.Combine(Root, "manifest.json");
            if (!File.Exists(path))
            {
                Debug.LogError($"[Nyx] manifest not found: {path}");
                enabled = false;
                return;
            }
            Manifest = JsonUtility.FromJson<NyxManifest>(File.ReadAllText(path));
            Debug.Log($"[Nyx] {Manifest.source}: {SnapshotCount} snapshots, grid " +
                      $"{Manifest.grid[0]}x{Manifest.grid[1]}x{Manifest.grid[2]}, {Manifest.num_particles} particles, " +
                      $"box {Manifest.box_size_mpc:F2} Mpc");

            if (preloadAll)
            {
                for (int s = 0; s < SnapshotCount; s++)
                {
                    for (int f = 0; f < Manifest.volume_fields.Length; f++) GetVolume(s, f);
                    if (Manifest.num_particles > 0) { GetPositions(s); GetVelocities(s); }
                }
            }
            UpdateIndices();
        }

        void Update()
        {
            if (!IsReady) return;
            HandleKeys();
            if (playing)
            {
                position += speed * Time.deltaTime;
                float last = SnapshotCount - 1;
                if (position > last)
                {
                    if (loop) position = 0f;
                    else { position = last; playing = false; }
                }
            }
            UpdateIndices();
        }

        void UpdateIndices()
        {
            position = Mathf.Clamp(position, 0f, SnapshotCount - 1);
            IndexA = Mathf.FloorToInt(position);
            IndexB = Mathf.Min(IndexA + 1, SnapshotCount - 1);
            Blend = position - IndexA;
        }

        void HandleKeys()
        {
            if (NyxInput.GetKeyDown(KeyCode.Space)) playing = !playing;
            if (NyxInput.GetKeyDown(KeyCode.RightArrow)) { playing = false; position = Mathf.Floor(position) + 1; }
            if (NyxInput.GetKeyDown(KeyCode.LeftArrow)) { playing = false; position = Mathf.Ceil(position) - 1; }
            if (NyxInput.GetKeyDown(KeyCode.Home)) position = 0;
        }

        // ------------------------------------------------------------------ data access

        public int FieldIndex(string fieldName)
        {
            if (!IsReady) return -1;
            return Array.FindIndex(Manifest.volume_fields, f => f.name == fieldName);
        }

        /// <summary>Redshift / age at the current (interpolated) position.</summary>
        public float CurrentRedshift => Mathf.Lerp(Manifest.snapshots[IndexA].z, Manifest.snapshots[IndexB].z, Blend);
        public float CurrentAgeGyr => Mathf.Lerp(Manifest.snapshots[IndexA].age_gyr, Manifest.snapshots[IndexB].age_gyr, Blend);

        public Texture3D GetVolume(int snap, int field)
        {
            if (_volumes.TryGetValue((snap, field), out var tex)) return tex;
            var g = Manifest.grid;
            byte[] bytes = File.ReadAllBytes(Path.Combine(Root, Manifest.snapshots[snap].volume_files[field]));
            int expected = g[0] * g[1] * g[2] * 2;
            if (bytes.Length != expected)
                Debug.LogError($"[Nyx] volume size mismatch ({bytes.Length} != {expected})");
            tex = new Texture3D(g[0], g[1], g[2], TextureFormat.RHalf, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = $"Nyx_snap{snap:00}_{Manifest.volume_fields[field].name}"
            };
            tex.SetPixelData(bytes, 0);
            tex.Apply(false, true); // upload and free CPU copy
            _volumes[(snap, field)] = tex;
            return tex;
        }

        public ComputeBuffer GetPositions(int snap) =>
            LoadFloat3Buffer(_positions, snap, Manifest.snapshots[snap].particle_positions);

        public ComputeBuffer GetVelocities(int snap) =>
            LoadFloat3Buffer(_velocities, snap, Manifest.snapshots[snap].particle_velocities);

        ComputeBuffer LoadFloat3Buffer(Dictionary<int, ComputeBuffer> cache, int snap, string rel)
        {
            if (cache.TryGetValue(snap, out var buf)) return buf;
            byte[] bytes = File.ReadAllBytes(Path.Combine(Root, rel));
            var floats = new float[bytes.Length / 4];
            Buffer.BlockCopy(bytes, 0, floats, 0, bytes.Length);
            buf = new ComputeBuffer(floats.Length / 3, sizeof(float) * 3);
            buf.SetData(floats);
            cache[snap] = buf;
            return buf;
        }

        void OnDestroy()
        {
            foreach (var t in _volumes.Values) if (t) Destroy(t);
            foreach (var b in _positions.Values) b.Release();
            foreach (var b in _velocities.Values) b.Release();
            _volumes.Clear(); _positions.Clear(); _velocities.Clear();
        }

        void OnGUI()
        {
            if (!showOverlay || !IsReady) return;
            var s = Manifest.snapshots;
            string text =
                $"z = {CurrentRedshift:F2}    age = {CurrentAgeGyr:F3} Gyr    snapshot {position + 1:F2} / {SnapshotCount}\n" +
                $"{Manifest.source}  {Manifest.grid[0]}^3  box {Manifest.box_size_mpc_h:F0} Mpc/h\n" +
                "[Space] play/pause   [<-/->] step   [Home] restart   [1-9] volume field   [P] particles";
            GUI.Label(new Rect(12, 10, 900, 70), text);
        }
    }
}
