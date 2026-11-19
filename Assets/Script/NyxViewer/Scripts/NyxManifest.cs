// Data classes matching manifest.json written by unity/tools/nyx_to_unity.py
using System;

namespace Nyx
{
    [Serializable]
    public class NyxFieldInfo
    {
        public string name;
        public string description;
        public string transform;
        public string unit;
        public float min;   // physical value = min + v * (max - min), v in [0,1]
        public float max;
    }

    [Serializable]
    public class NyxSnapshotInfo
    {
        public int index;
        public string plotfile;
        public int step;
        public float z;
        public float a;
        public float age_gyr;
        public string[] volume_files;      // same order as NyxManifest.volume_fields
        public string particle_positions;  // float32 xyz, box units [0,1), sorted by particle id
        public string particle_velocities; // float32 xyz, km/s
        public float particle_speed_max;
    }

    [Serializable]
    public class NyxCosmology
    {
        public float omega_m;
        public float omega_b;
        public float h;
    }

    [Serializable]
    public class NyxManifest
    {
        public int format_version;
        public string source;
        public float box_size_mpc;
        public float box_size_mpc_h;
        public int[] grid;
        public int num_particles;
        public int particle_stride;
        public NyxCosmology cosmology;
        public NyxFieldInfo[] volume_fields;
        public NyxSnapshotInfo[] snapshots;
    }
}
