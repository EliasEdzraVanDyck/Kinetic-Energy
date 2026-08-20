using System.Collections.Generic;
using UnityEngine;

namespace KineticEnergy.Player
{

    public class ImpactCrackDecals : MonoBehaviour
    {
        [Header("Sheet")]
        [Tooltip("Transparent material whose _BaseMap holds the crack sheet - wired by KineticEnergySetup.")]
        public Material decalMaterial;
        public int sheetColumns = 3;
        public int sheetRows = 3;

        [Header("Placement")]
        [Tooltip("World-space edge length of a spawned crack quad.")]
        public float decalSize = 1.2f;

        public float surfaceOffset = 0.02f;
        [Tooltip("Give each crack a random spin around the surface normal so repeats are less obvious.")]
        public bool randomRoll = true;

        public float minImpactSpeed = 1.5f;
        [Tooltip("Ignore additional contacts this soon after the last decal - one crash can report several contacts at once.")]
        public float minSpawnInterval = 0.05f;

        [Header("Lifetime")]
        public float holdSeconds = 3f;
        public float fadeSeconds = 1f;
        [Tooltip("Oldest decals are removed early once this many exist.")]
        public int maxDecals = 60;

        class Decal
        {
            public GameObject go;
            public MeshRenderer renderer;
            public MaterialPropertyBlock properties;
            public float age;
        }

        static readonly int BaseMapStId = Shader.PropertyToID("_BaseMap_ST");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        static Mesh sharedQuad;

        readonly List<Decal> decals = new List<Decal>();
        Transform container;
        float lastSpawnTime = float.NegativeInfinity;

        void OnCollisionEnter(Collision collision)
        {
            if (decalMaterial == null || collision.contactCount == 0) return;
            if (Time.time - lastSpawnTime < minSpawnInterval) return;

            ContactPoint contact = collision.GetContact(0);
            float impactSpeed = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, contact.normal));
            if (impactSpeed < minImpactSpeed) return;

            lastSpawnTime = Time.time;
            Spawn(contact.point, contact.normal);
        }

        void Spawn(Vector3 point, Vector3 normal)
        {
            if (container == null)
            {

                container = new GameObject("CrackDecals").transform;
            }

            Vector3 tangent = Vector3.Cross(normal, Vector3.up);
            if (tangent.sqrMagnitude < 1e-4f) tangent = Vector3.Cross(normal, Vector3.forward);
            Quaternion rotation = Quaternion.LookRotation(normal, tangent.normalized);
            if (randomRoll) rotation = Quaternion.AngleAxis(Random.Range(0f, 360f), normal) * rotation;

            GameObject go = new GameObject("CrackDecal");
            go.transform.SetParent(container, false);
            go.transform.SetPositionAndRotation(point + normal * surfaceOffset, rotation);
            go.transform.localScale = Vector3.one * decalSize;

            MeshFilter filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = GetQuadMesh();
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = decalMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            int columns = Mathf.Max(sheetColumns, 1);
            int rows = Mathf.Max(sheetRows, 1);
            int column = Random.Range(0, columns);
            int row = Random.Range(0, rows);

            MaterialPropertyBlock properties = new MaterialPropertyBlock();
            properties.SetVector(BaseMapStId, new Vector4(1f / columns, 1f / rows, column / (float)columns, row / (float)rows));
            properties.SetColor(BaseColorId, Color.white);
            renderer.SetPropertyBlock(properties);

            decals.Add(new Decal { go = go, renderer = renderer, properties = properties, age = 0f });

            while (decals.Count > Mathf.Max(maxDecals, 1))
            {
                Destroy(decals[0].go);
                decals.RemoveAt(0);
            }
        }

        void Update()
        {
            for (int i = decals.Count - 1; i >= 0; i--)
            {
                Decal decal = decals[i];
                decal.age += Time.deltaTime;
                if (decal.age <= holdSeconds) continue;

                float alpha = fadeSeconds > 0f ? 1f - (decal.age - holdSeconds) / fadeSeconds : 0f;
                if (alpha <= 0f)
                {
                    Destroy(decal.go);
                    decals.RemoveAt(i);
                    continue;
                }

                decal.properties.SetColor(BaseColorId, new Color(1f, 1f, 1f, alpha));
                decal.renderer.SetPropertyBlock(decal.properties);
            }
        }

        static Mesh GetQuadMesh()
        {
            if (sharedQuad != null) return sharedQuad;

            sharedQuad = new Mesh { name = "CrackDecalQuad" };
            sharedQuad.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
            };
            sharedQuad.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
            };
            sharedQuad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            sharedQuad.RecalculateNormals();
            sharedQuad.RecalculateBounds();
            return sharedQuad;
        }
    }
}
