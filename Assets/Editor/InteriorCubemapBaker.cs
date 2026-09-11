using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace InteriorMapping.EditorTools
{
    /// <summary>
    /// Bakes the cubemap sampled by the <c>InteriorMapping/SingleFile</c> shader.
    /// </summary>
    /// <remarks>
    /// The room is built in code so the cubemap is reproducible from a clean clone. It is a cube
    /// because the shader looks the interior up with a direction from the centre of a unit box.
    /// </remarks>
    public static class InteriorCubemapBaker
    {
        private const string CubemapAssetPath = "Assets/Textures/InteriorRoom.cubemap";
        private const string MaterialAssetPath = "Assets/Materials/InteriorMapping.mat";
        private const string CubemapPropertyName = "_InteriorCubemap";
        private const string LitShaderName = "Universal Render Pipeline/Lit";

        // The interior is only seen through a window-sized slice of the box, never full screen.
        private const int FaceSize = 512;

        // Only the proportions reach the shader; a real size keeps the light falloff sane.
        private const float RoomSize = 4f;
        private const float WallThickness = 0.1f;

        // The room is sealed, but building it far out also guards against large scene objects.
        private static readonly Vector3 BakeOrigin = new Vector3(0f, -5000f, 0f);

        [MenuItem("Tools/Interior Mapping/Bake Interior Cubemap")]
        public static void Bake()
        {
            GameObject room = null;
            Camera probeCamera = null;
            RenderTexture cubemapRenderTarget = null;
            List<Light> suppressedLights = null;

            try
            {
                room = BuildRoom();
                probeCamera = CreateProbeCamera();
                suppressedLights = SuppressForeignLights(room);

                cubemapRenderTarget = new RenderTexture(FaceSize, FaceSize, 24, RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.sRGB)
                {
                    dimension = TextureDimension.Cube,
                    useMipMap = false,
                    name = "InteriorCubemapBakeTarget"
                };
                cubemapRenderTarget.Create();

                // Engine-driven so cubemap handedness and per-platform vertical flip stay correct.
                if (!probeCamera.RenderToCubemap(cubemapRenderTarget))
                {
                    Debug.LogError("Camera.RenderToCubemap returned false - nothing was baked.");
                    return;
                }

                Cubemap bakedAsset = SaveOrReplaceAsset(ReadBack(cubemapRenderTarget));
                AssignToMaterial(bakedAsset);

                Debug.Log($"Baked interior cubemap to {CubemapAssetPath} ({FaceSize}px faces).", bakedAsset);
                EditorGUIUtility.PingObject(bakedAsset);
            }
            finally
            {
                RestoreLights(suppressedLights);

                if (cubemapRenderTarget != null)
                {
                    cubemapRenderTarget.Release();
                    Object.DestroyImmediate(cubemapRenderTarget);
                }

                if (probeCamera != null)
                {
                    Object.DestroyImmediate(probeCamera.gameObject);
                }

                DestroyRoom(room);
            }
        }

        private static GameObject BuildRoom()
        {
            GameObject root = new GameObject("InteriorBakeRoom") { hideFlags = HideFlags.HideAndDontSave };
            root.transform.position = BakeOrigin;

            float halfRoomSize = RoomSize * 0.5f;
            Vector3 floorAndCeilingScale = new Vector3(RoomSize, WallThickness, RoomSize);
            Vector3 zFacingWallScale = new Vector3(RoomSize, RoomSize, WallThickness);
            Vector3 xFacingWallScale = new Vector3(WallThickness, RoomSize, RoomSize);

            // Bright ceiling over dark floor sells this as a lit room, and per-wall tints make a
            // mis-oriented face obvious on sight.
            AddBox(root, "Floor", new Vector3(0f, -halfRoomSize, 0f), floorAndCeilingScale,
                new Color(0.26f, 0.19f, 0.14f));
            AddBox(root, "Ceiling", new Vector3(0f, halfRoomSize, 0f), floorAndCeilingScale,
                new Color(0.92f, 0.90f, 0.86f));
            AddBox(root, "Wall -Z", new Vector3(0f, 0f, -halfRoomSize), zFacingWallScale,
                new Color(0.62f, 0.60f, 0.57f));
            AddBox(root, "Wall +Z", new Vector3(0f, 0f, halfRoomSize), zFacingWallScale,
                new Color(0.45f, 0.48f, 0.54f));
            AddBox(root, "Wall -X", new Vector3(-halfRoomSize, 0f, 0f), xFacingWallScale,
                new Color(0.58f, 0.54f, 0.48f));
            AddBox(root, "Wall +X", new Vector3(halfRoomSize, 0f, 0f), xFacingWallScale,
                new Color(0.52f, 0.55f, 0.58f));

            // Parallax against near objects is what reads as depth; an empty box gives none.
            AddBox(root, "Table", new Vector3(0.7f, -halfRoomSize + 0.75f, 0.9f),
                new Vector3(1.4f, 0.1f, 0.8f), new Color(0.35f, 0.24f, 0.16f));
            AddBox(root, "Shelf", new Vector3(-halfRoomSize + 0.25f, 0.4f, 0f),
                new Vector3(0.4f, 0.08f, 2f), new Color(0.33f, 0.22f, 0.15f));
            AddBox(root, "Crate", new Vector3(-1.1f, -halfRoomSize + 0.35f, -1f),
                new Vector3(0.7f, 0.7f, 0.7f), new Color(0.40f, 0.33f, 0.24f));

            AddRoomLight(root, new Vector3(0f, halfRoomSize - 0.6f, 0f));
            return root;
        }

        private static void AddBox(GameObject parent, string name, Vector3 localPosition, Vector3 scale, Color color)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.hideFlags = HideFlags.HideAndDontSave;
            box.transform.SetParent(parent.transform, worldPositionStays: false);
            box.transform.localPosition = localPosition;
            box.transform.localScale = scale;

            // Nothing here is simulated.
            Object.DestroyImmediate(box.GetComponent<Collider>());

            Material material = new Material(Shader.Find(LitShaderName)) { hideFlags = HideFlags.HideAndDontSave };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", 0.1f);
            box.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        private static void AddRoomLight(GameObject parent, Vector3 localPosition)
        {
            GameObject lightObject = new GameObject("Room Light") { hideFlags = HideFlags.HideAndDontSave };
            lightObject.transform.SetParent(parent.transform, worldPositionStays: false);
            lightObject.transform.localPosition = localPosition;

            Light roomLight = lightObject.AddComponent<Light>();
            roomLight.type = LightType.Point;
            roomLight.color = new Color(1f, 0.93f, 0.82f);
            roomLight.intensity = 18f;
            roomLight.range = RoomSize * 1.8f;
            roomLight.shadows = LightShadows.Soft;
        }

        private static Camera CreateProbeCamera()
        {
            GameObject probeObject = new GameObject("InteriorBakeProbe") { hideFlags = HideFlags.HideAndDontSave };
            probeObject.transform.position = BakeOrigin;

            Camera probeCamera = probeObject.AddComponent<Camera>();
            probeCamera.enabled = false; // Rendered on demand only.
            probeCamera.clearFlags = CameraClearFlags.SolidColor;
            probeCamera.backgroundColor = Color.black;
            probeCamera.nearClipPlane = 0.01f;
            probeCamera.farClipPlane = RoomSize * 4f;
            probeCamera.fieldOfView = 90f;
            probeCamera.aspect = 1f;

            // URP would add this on first render; explicit is clearer.
            probeObject.AddComponent<UniversalAdditionalCameraData>();
            return probeCamera;
        }

        private static List<Light> SuppressForeignLights(GameObject room)
        {
            // Otherwise the bake depends on whichever scene happens to be open.
            List<Light> suppressedLights = new List<Light>();

            foreach (Light sceneLight in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (!sceneLight.enabled || sceneLight.transform.IsChildOf(room.transform))
                {
                    continue;
                }

                sceneLight.enabled = false;
                suppressedLights.Add(sceneLight);
            }

            return suppressedLights;
        }

        private static void RestoreLights(List<Light> suppressedLights)
        {
            if (suppressedLights == null)
            {
                return;
            }

            foreach (Light suppressedLight in suppressedLights)
            {
                if (suppressedLight != null)
                {
                    suppressedLight.enabled = true;
                }
            }
        }

        private static Cubemap ReadBack(RenderTexture cubemapRenderTarget)
        {
            // ReadPixels only speaks 2D, so each face comes back through a scratch texture.
            // No mips: the shader samples at an explicit LOD 0.
            Cubemap cubemap = new Cubemap(FaceSize, TextureFormat.RGBA32, mipChain: false);
            Texture2D faceReadbackTexture = new Texture2D(FaceSize, FaceSize, TextureFormat.RGBA32, mipChain: false);
            RenderTexture previousRenderTarget = RenderTexture.active;

            try
            {
                for (int faceIndex = 0; faceIndex < 6; faceIndex++)
                {
                    Graphics.SetRenderTarget(cubemapRenderTarget, 0, (CubemapFace)faceIndex);
                    faceReadbackTexture.ReadPixels(new Rect(0f, 0f, FaceSize, FaceSize), 0, 0,
                        recalculateMipMaps: false);
                    faceReadbackTexture.Apply(updateMipmaps: false);
                    cubemap.SetPixels(faceReadbackTexture.GetPixels(), (CubemapFace)faceIndex);
                }

                cubemap.Apply(updateMipmaps: false);
            }
            finally
            {
                RenderTexture.active = previousRenderTarget;
                Object.DestroyImmediate(faceReadbackTexture);
            }

            return cubemap;
        }

        private static Cubemap SaveOrReplaceAsset(Cubemap bakedCubemap)
        {
            string directory = Path.GetDirectoryName(CubemapAssetPath);

            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                AssetDatabase.Refresh();
            }

            Cubemap existingAsset = AssetDatabase.LoadAssetAtPath<Cubemap>(CubemapAssetPath);
            bool canOverwriteInPlace = existingAsset != null && existingAsset.width == FaceSize &&
                                       existingAsset.format == bakedCubemap.format;

            // CreateAsset over an existing path mints a new GUID, breaking the material slot below.
            if (canOverwriteInPlace)
            {
                for (int faceIndex = 0; faceIndex < 6; faceIndex++)
                {
                    existingAsset.SetPixels(bakedCubemap.GetPixels((CubemapFace)faceIndex), (CubemapFace)faceIndex);
                }

                existingAsset.Apply(updateMipmaps: false);
                EditorUtility.SetDirty(existingAsset);
                AssetDatabase.SaveAssets();
                Object.DestroyImmediate(bakedCubemap);
                return existingAsset;
            }

            AssetDatabase.CreateAsset(bakedCubemap, CubemapAssetPath);
            AssetDatabase.SaveAssets();
            return bakedCubemap;
        }

        private static void AssignToMaterial(Cubemap cubemap)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialAssetPath);

            if (material == null)
            {
                Debug.LogWarning($"No material at {MaterialAssetPath}; the cubemap was baked but not assigned.");
                return;
            }

            if (!material.HasProperty(CubemapPropertyName))
            {
                Debug.LogWarning($"'{material.name}' has no {CubemapPropertyName} property. " +
                                 "Is the InteriorMapping/SingleFile shader assigned to it?");
                return;
            }

            material.SetTexture(CubemapPropertyName, cubemap);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
        }

        private static void DestroyRoom(GameObject room)
        {
            if (room == null)
            {
                return;
            }

            // The per-box materials are owned by nothing else and would otherwise leak.
            foreach (MeshRenderer meshRenderer in room.GetComponentsInChildren<MeshRenderer>(includeInactive: true))
            {
                if (meshRenderer.sharedMaterial != null)
                {
                    Object.DestroyImmediate(meshRenderer.sharedMaterial);
                }
            }

            Object.DestroyImmediate(room);
        }
    }
}
