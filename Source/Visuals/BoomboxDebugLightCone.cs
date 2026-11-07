using System;
using UnityEngine;
using WorkableBoombox.Config;

namespace WorkableBoombox.Visuals
{
    public class BoomboxDebugLightCone : MonoBehaviour
    {
        private Light targetLight;
        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private LineRenderer lineRenderer;
        private Mesh coneMesh;
        private Material coneMaterial;
        private Material lineMaterial;

        private float lastSpotAngle = -1f;
        private float lastRange = -1f;

        private const int SEGMENTS = 24;
        private const float CONE_LENGTH = 3.5f;

        public void Initialize(Light lightSource)
        {
            targetLight = lightSource;
            SetupComponents();
            RebuildConeMesh();
        }

        private void SetupComponents()
        {
            if (targetLight == null)
            {
                targetLight = GetComponent<Light>();
            }

            // 1. Mesh для полупрозрачной объемной поверхности конуса
            GameObject meshObj = new GameObject("DebugCone_Surface");
            meshObj.transform.SetParent(transform, false);
            meshObj.transform.localPosition = Vector3.zero;
            meshObj.transform.localRotation = Quaternion.identity;
            meshObj.transform.localScale = Vector3.one;

            meshFilter = meshObj.AddComponent<MeshFilter>();
            meshRenderer = meshObj.AddComponent<MeshRenderer>();

            Shader transparentShader = Shader.Find("Sprites/Default");
            if (transparentShader == null) transparentShader = Shader.Find("GUI/Text Shader");
            if (transparentShader == null) transparentShader = Shader.Find("Standard");

            coneMaterial = new Material(transparentShader);
            coneMaterial.renderQueue = 3000;
            meshRenderer.material = coneMaterial;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            // 2. LineRenderer для четких контурных линий (лучи + обод)
            GameObject lineObj = new GameObject("DebugCone_Lines");
            lineObj.transform.SetParent(transform, false);
            lineObj.transform.localPosition = Vector3.zero;
            lineObj.transform.localRotation = Quaternion.identity;
            lineObj.transform.localScale = Vector3.one;

            lineRenderer = lineObj.AddComponent<LineRenderer>();
            lineMaterial = new Material(transparentShader);
            lineRenderer.material = lineMaterial;
            lineRenderer.useWorldSpace = false;
            lineRenderer.startWidth = 0.02f;
            lineRenderer.endWidth = 0.02f;
            lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lineRenderer.receiveShadows = false;
        }

        public void UpdateVisuals(Color lightColor, float spotAngle, float range, bool isLightActive)
        {
            bool showCone = isLightActive && (ModConfig.EnableDebugLightCones == null || ModConfig.EnableDebugLightCones.Value);

            if (meshRenderer != null) meshRenderer.enabled = showCone;
            if (lineRenderer != null) lineRenderer.enabled = showCone;

            if (!showCone)
            {
                return;
            }

            if (Mathf.Abs(spotAngle - lastSpotAngle) > 0.1f || Mathf.Abs(range - lastRange) > 0.1f)
            {
                lastSpotAngle = spotAngle;
                lastRange = range;
                RebuildConeMesh();
            }

            // Полупрозрачный цвет для граней конуса и яркий для контурных линий
            Color surfaceColor = new Color(lightColor.r, lightColor.g, lightColor.b, 0.22f);
            Color lineColor = new Color(lightColor.r, lightColor.g, lightColor.b, 0.90f);

            if (coneMaterial != null) coneMaterial.color = surfaceColor;
            if (lineRenderer != null)
            {
                lineRenderer.startColor = lineColor;
                lineRenderer.endColor = lineColor;
            }
        }

        private void RebuildConeMesh()
        {
            float angle = targetLight != null ? targetLight.spotAngle : (lastSpotAngle > 0 ? lastSpotAngle : 85f);
            float length = Mathf.Clamp(targetLight != null ? targetLight.range * 0.45f : CONE_LENGTH, 1.5f, 4.0f);
            float radius = length * Mathf.Tan(angle * 0.5f * Mathf.Deg2Rad);

            // Вершины: 0 - вершина конуса (источник света)
            Vector3[] vertices = new Vector3[SEGMENTS + 1];
            vertices[0] = Vector3.zero;

            for (int i = 0; i < SEGMENTS; i++)
            {
                float rad = (i / (float)SEGMENTS) * Mathf.PI * 2f;
                float x = Mathf.Cos(rad) * radius;
                float y = Mathf.Sin(rad) * radius;
                vertices[i + 1] = new Vector3(x, y, length);
            }

            // Двусторонние треугольники поверхности (чтобы конус был виден и снаружи, и изнутри)
            int[] triangles = new int[SEGMENTS * 6];
            for (int i = 0; i < SEGMENTS; i++)
            {
                int current = i + 1;
                int next = ((i + 1) % SEGMENTS) + 1;

                // Внешняя сторона
                triangles[i * 6 + 0] = 0;
                triangles[i * 6 + 1] = current;
                triangles[i * 6 + 2] = next;

                // Внутренняя сторона
                triangles[i * 6 + 3] = 0;
                triangles[i * 6 + 4] = next;
                triangles[i * 6 + 5] = current;
            }

            if (coneMesh == null)
            {
                coneMesh = new Mesh();
                coneMesh.name = "Boombox_DebugConeMesh";
            }
            else
            {
                coneMesh.Clear();
            }

            coneMesh.vertices = vertices;
            coneMesh.triangles = triangles;
            coneMesh.RecalculateNormals();

            if (meshFilter != null) meshFilter.mesh = coneMesh;

            // Контурные линии LineRenderer:
            // Апекс -> Окружность основания -> 4 луча обратно к апексу
            int lineCount = 1 + (SEGMENTS + 1) + 8;
            Vector3[] linePoints = new Vector3[lineCount];
            int p = 0;

            linePoints[p++] = Vector3.zero; // Апекс
            for (int i = 0; i <= SEGMENTS; i++)
            {
                int seg = i % SEGMENTS;
                float rad = (seg / (float)SEGMENTS) * Mathf.PI * 2f;
                linePoints[p++] = new Vector3(Mathf.Cos(rad) * radius, Mathf.Sin(rad) * radius, length);
            }

            // 4 продольных направляющих луча: 0°, 90°, 180°, 270°
            int[] rayIndices = new int[] { 0, SEGMENTS / 4, SEGMENTS / 2, (SEGMENTS * 3) / 4 };
            foreach (int idx in rayIndices)
            {
                float rad = (idx / (float)SEGMENTS) * Mathf.PI * 2f;
                linePoints[p++] = Vector3.zero;
                linePoints[p++] = new Vector3(Mathf.Cos(rad) * radius, Mathf.Sin(rad) * radius, length);
            }

            if (lineRenderer != null)
            {
                lineRenderer.positionCount = linePoints.Length;
                lineRenderer.SetPositions(linePoints);
            }
        }

        private void OnDestroy()
        {
            if (coneMesh != null) Destroy(coneMesh);
            if (coneMaterial != null) Destroy(coneMaterial);
            if (lineMaterial != null) Destroy(lineMaterial);
        }
    }
}
