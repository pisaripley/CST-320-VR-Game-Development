using UnityEngine;
using System.Collections.Generic;

// GPU instancing + manual LOD demo for Skog.
// Optimized to precompute matrices and reduce per-frame work.
// (Generated using ChatGPT)

namespace Skog.Demo
{
    public class GPUInstancingDemo : MonoBehaviour
    {
        [Header("LOD Meshes & Materials")]
        public Mesh lod0Mesh;           // Highest detail
        public Mesh lod1Mesh;           // Medium detail
        public Mesh lod2Mesh;           // Lowest detail / billboard

        public Material lod0Material;
        public Material lod1Material;
        public Material lod2Material;

        [Header("LOD Distances")]
        public float lod1Distance = 50f;   // beyond this, use LOD1
        public float lod2Distance = 200f;  // beyond this, use LOD2

        [Header("Instance Count")]
        public int count = 5000;

        [Header("Spawn Area (local to this object)")]
        public Vector3 areaSize = new Vector3(200f, 0f, 200f);

        [Header("Ground Raycast")]
        public float raycastHeight = 50f;
        public float raycastMaxDistance = 200f;
        public LayerMask groundMask = ~0;

        [Header("Tree Randomization")]
        public float minUniformScale = 0.9f;
        public float maxUniformScale = 1.1f;
        public bool alignToGroundNormal = true;

        [Header("LOD Update")]
        [Tooltip("How many frames between LOD bucket recalculations. 1 = every frame.")]
        public int lodUpdateInterval = 1;

        private struct Instance
        {
            public Vector3 position;
            public Matrix4x4 matrix; // precomputed TRS
        }

        // All instances (we decide LOD per-frame)
        private readonly List<Instance> instances = new List<Instance>();

        // Per-LOD temp buffers for this frame
        private readonly List<Matrix4x4> lod0Matrices = new List<Matrix4x4>();
        private readonly List<Matrix4x4> lod1Matrices = new List<Matrix4x4>();
        private readonly List<Matrix4x4> lod2Matrices = new List<Matrix4x4>();

        private Matrix4x4[] batchArray = new Matrix4x4[1023];
        private int lodFrameCounter = 0;

        void Start()
        {
            // Basic validation
            if (!lod0Mesh || !lod0Material)
            {
                Debug.LogWarning("[GPUInstancingDemo] LOD0 mesh or material not assigned.");
                enabled = false;
                return;
            }

            // Fallbacks: if LOD1/2 are not set, reuse LOD0
            if (!lod1Mesh) lod1Mesh = lod0Mesh;
            if (!lod2Mesh) lod2Mesh = lod1Mesh ? lod1Mesh : lod0Mesh;
            if (!lod1Material) lod1Material = lod0Material;
            if (!lod2Material) lod2Material = lod1Material ? lod1Material : lod0Material;

            lod0Material.enableInstancing = true;
            lod1Material.enableInstancing = true;
            lod2Material.enableInstancing = true;

            // Reserve capacities to avoid list resizing at runtime
            if (count > 0)
            {
                instances.Capacity = count;
                lod0Matrices.Capacity = count;
                lod1Matrices.Capacity = count;
                lod2Matrices.Capacity = count;
            }

            if (lodUpdateInterval < 1) lodUpdateInterval = 1;

            GenerateInstances();
        }

        void GenerateInstances()
        {
            instances.Clear();

            var rnd = new System.Random();
            int placed = 0;
            int safety = 0;

            while (placed < count && safety < count * 10)
            {
                safety++;

                // Random position in local space within areaSize
                float x = (float)rnd.NextDouble() * areaSize.x - areaSize.x * 0.5f;
                float z = (float)rnd.NextDouble() * areaSize.z - areaSize.z * 0.5f;

                Vector3 localPos = new Vector3(x, 0f, z);
                Vector3 rayOrigin = transform.TransformPoint(localPos + Vector3.up * raycastHeight);

                if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, raycastMaxDistance, groundMask, QueryTriggerInteraction.Ignore))
                {
                    Vector3 pos = hit.point;

                    // Random Y rotation
                    float yRot = rnd.Next(0, 360);
                    Quaternion rotY = Quaternion.Euler(0f, yRot, 0f);

                    Quaternion finalRot;
                    if (alignToGroundNormal)
                    {
                        Quaternion align = Quaternion.FromToRotation(Vector3.up, hit.normal);
                        finalRot = align * rotY;
                    }
                    else
                    {
                        finalRot = rotY;
                    }

                    // Random uniform scale
                    float s = Mathf.Lerp(minUniformScale, maxUniformScale, (float)rnd.NextDouble());
                    Vector3 scale = Vector3.one * s;

                    Matrix4x4 m = Matrix4x4.TRS(pos, finalRot, scale);

                    instances.Add(new Instance
                    {
                        position = pos,
                        matrix = m
                    });

                    placed++;
                }
            }

            if (placed < count)
            {
                Debug.LogWarning($"[GPUInstancingDemo] Only placed {placed} of {count} trees (maybe no ground under some rays?).");
            }
        }

        void Update()
        {
            if (instances.Count == 0)
                return;

            Camera cam = Camera.main;
            if (!cam)
                return;

            // Recompute LOD buckets only every N frames
            lodFrameCounter++;
            if (lodFrameCounter >= lodUpdateInterval)
            {
                lodFrameCounter = 0;
                RebuildLODBuckets(cam.transform.position);
            }

            // Draw all LOD buckets using last computed lists
            DrawLOD(lod0Mesh, lod0Material, lod0Matrices);
            DrawLOD(lod1Mesh, lod1Material, lod1Matrices);
            DrawLOD(lod2Mesh, lod2Material, lod2Matrices);
        }

        void RebuildLODBuckets(Vector3 camPos)
        {
            float lod1DistSqr = lod1Distance * lod1Distance;
            float lod2DistSqr = lod2Distance * lod2Distance;

            lod0Matrices.Clear();
            lod1Matrices.Clear();
            lod2Matrices.Clear();

            foreach (var inst in instances)
            {
                float distSqr = (inst.position - camPos).sqrMagnitude;
                Matrix4x4 m = inst.matrix; // precomputed

                if (distSqr < lod1DistSqr)
                {
                    lod0Matrices.Add(m);
                }
                else if (distSqr < lod2DistSqr)
                {
                    lod1Matrices.Add(m);
                }
                else
                {
                    lod2Matrices.Add(m);
                }
            }
        }

        void DrawLOD(Mesh mesh, Material mat, List<Matrix4x4> mats)
        {
            if (!mesh || !mat || mats.Count == 0)
                return;

            int total = mats.Count;
            int index = 0;

            while (index < total)
            {
                int batchSize = Mathf.Min(1023, total - index);

                for (int i = 0; i < batchSize; i++)
                {
                    batchArray[i] = mats[index + i];
                }

                Graphics.DrawMeshInstanced(mesh, 0, mat, batchArray, batchSize);
                index += batchSize;
            }
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0f, 1f, 0f, 0.25f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, areaSize);
        }
    }
}