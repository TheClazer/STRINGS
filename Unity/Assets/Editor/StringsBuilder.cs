using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Strings;

namespace Strings.Editor
{
    /// <summary>
    /// Builds and runs the STRINGS scene.
    ///   Unity -batchmode -projectPath ...\RoomIsLying_v2 -executeMethod Strings.Editor.StringsBuilder.BuildScene
    ///   Unity -batchmode ... -executeMethod Strings.Editor.StringsBuilder.Autopilot -str-auto -str-video [-str-count N] [-str-timeout S]
    /// </summary>
    public static class StringsBuilder
    {
        public const string ScenePath = "Assets/Strings/Strings_Main.unity";

        static Material MatAsset(string name, string shader)
        {
            Directory.CreateDirectory("Assets/Strings/Materials");
            string p = $"Assets/Strings/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null) { m = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(m, p); }
            m.shader = Shader.Find(shader); EditorUtility.SetDirty(m); return m;
        }

        [MenuItem("Strings/Build Scene")]
        public static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // seated player rig: eye level origin so the head is where the rig is, with or without tracking
            var rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab");
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab); rig.name = "OVRCameraRig"; rig.transform.position = new Vector3(0f, 1.15f, 0f);
            var mgr = rig.GetComponent<OVRManager>(); mgr.isInsightPassthroughEnabled = false; mgr.trackingOriginType = OVRManager.TrackingOrigin.EyeLevel;
            var pt = rig.GetComponent<OVRPassthroughLayer>(); if (pt != null) Object.DestroyImmediate(pt);
            var eye = rig.transform.Find("TrackingSpace/CenterEyeAnchor"); var cam = eye.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black; cam.nearClipPlane = 0.03f; eye.gameObject.tag = "MainCamera";
            var ud = cam.GetComponent<UniversalAdditionalCameraData>() ?? cam.gameObject.AddComponent<UniversalAdditionalCameraData>(); ud.renderPostProcessing = true;
            // hands: the SDK's OpenXR hand prefabs, matching the project's hand skeleton version (OculusRuntimeSettings handSkeletonVersion = OpenXR).
            // The legacy OVRCustomHandPrefab_* are built for the OVR skeleton. StringsInput reads either joint-id set (it checks the skeleton type).
            // The skinned hand mesh is left as the SDK sets it up; SHandsView shows it only while that hand is tracked.
            // OVRHand converts its OVRSkeleton to the global version only in OnValidate (editor, and not before the editor is ready, e.g. in
            // batch mode). A player keeps the serialized type, and OVRSkeleton only accepts an OVRHand data provider whose type matches, so
            // the type is written here explicitly; a mismatch would leave IsDataValid false and the game would never see a hand.
            bool openxr = OVRRuntimeSettings.GetRuntimeSettings().HandSkeletonVersion != OVRHandSkeletonVersion.OVR;
            Debug.Log("[Strings] hand skeleton version (OculusRuntimeSettings): " + OVRRuntimeSettings.GetRuntimeSettings().HandSkeletonVersion);
            foreach (var (anchor, left) in new[] { ("TrackingSpace/LeftHandAnchor", true), ("TrackingSpace/RightHandAnchor", false) })
            {
                string prefab = (openxr ? "OpenXRCustomHandPrefab_" : "OVRCustomHandPrefab_") + (left ? "L" : "R");
                var a = rig.transform.Find(anchor); var hp = AssetDatabase.LoadAssetAtPath<GameObject>($"Packages/com.meta.xr.sdk.core/Prefabs/{prefab}.prefab");
                if (a == null || hp == null) { Debug.LogError("[Strings] missing hand " + prefab); continue; }
                var h = (GameObject)PrefabUtility.InstantiatePrefab(hp, a);
                // Measured on a Quest 3S (2026-11-18): the player crashed ~7 s after launch with a null dereference in
                // SkinnedMeshRenderer::UpdateCachedMesh during first-scene load (AwakeFromLoad). The hand prefabs' skinned meshes are the only
                // skinned renderers in this scene, so they are removed here; tracking (OVRHand + OVRSkeleton) is kept, and SHandsView draws the
                // hands from the tracked wrist and finger curls instead.
                PrefabUtility.UnpackPrefabInstance(h, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                foreach (var smr in h.GetComponentsInChildren<SkinnedMeshRenderer>(true)) Object.DestroyImmediate(smr);
                Debug.Log("[Strings] hand " + prefab + ": skinned meshes removed, remaining SkinnedMeshRenderers " + h.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length);
                var want = openxr ? (left ? OVRSkeleton.SkeletonType.XRHandLeft : OVRSkeleton.SkeletonType.XRHandRight) : (left ? OVRSkeleton.SkeletonType.HandLeft : OVRSkeleton.SkeletonType.HandRight);
                foreach (var sk in h.GetComponentsInChildren<OVRSkeleton>(true))
                {
                    var so = new SerializedObject(sk); var p = so.FindProperty("_skeletonType");
                    if (p != null) { p.intValue = (int)want; so.ApplyModifiedPropertiesWithoutUndo(); PrefabUtility.RecordPrefabInstancePropertyModifications(sk); EditorUtility.SetDirty(sk); }
                    Debug.Log("[Strings] " + prefab + ": " + sk.GetType().Name + " skeleton type set to " + want + " (now " + sk.GetSkeletonType() + ")");
                }
                foreach (var mesh in h.GetComponentsInChildren<OVRMesh>(true))
                {
                    var so = new SerializedObject(mesh); var p = so.FindProperty("_meshType");
                    var wantMesh = openxr ? (left ? OVRMesh.MeshType.XRHandLeft : OVRMesh.MeshType.XRHandRight) : (left ? OVRMesh.MeshType.HandLeft : OVRMesh.MeshType.HandRight);
                    if (p != null) { p.intValue = (int)wantMesh; so.ApplyModifiedPropertiesWithoutUndo(); PrefabUtility.RecordPrefabInstancePropertyModifications(mesh); }
                }
                Debug.Log("[Strings] hand prefab " + prefab + " under " + anchor + ", renderers " + h.GetComponentsInChildren<Renderer>(true).Length);
            }

            // grade: filmic, a little bloom, vignette (the painterly pass comes from the renderer)
            var vol = new GameObject("PostFX").AddComponent<Volume>(); vol.isGlobal = true;
            var prof = ScriptableObject.CreateInstance<VolumeProfile>();
            var bl = prof.Add<Bloom>(); bl.intensity.Override(0.9f); bl.threshold.Override(0.9f);
            var tm = prof.Add<Tonemapping>(); tm.mode.Override(TonemappingMode.ACES);
            var vg = prof.Add<Vignette>(); vg.intensity.Override(0.38f);
            var ca = prof.Add<ColorAdjustments>(); ca.postExposure.Override(0f); ca.contrast.Override(14f); ca.saturation.Override(14f);
            Directory.CreateDirectory("Assets/Strings"); AssetDatabase.DeleteAsset("Assets/Strings/PostFX.asset"); AssetDatabase.CreateAsset(prof, "Assets/Strings/PostFX.asset");
            // the effects must be saved inside the profile asset; without this the player gets a profile of null components and URP throws in
            // VolumeManager.OverrideData every frame, so the camera never renders (measured on a Quest 3S, 2026-11-18: stuck on the splash)
            foreach (var comp in prof.components) { comp.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy; AssetDatabase.AddObjectToAsset(comp, prof); }
            EditorUtility.SetDirty(prof); AssetDatabase.SaveAssets(); vol.sharedProfile = prof;
            Debug.Log("[Strings] PostFX profile saved with " + prof.components.Count + " effects");

            var go = new GameObject("StringsGame");
            var g = go.AddComponent<StringsGame>();
            g.lit = MatAsset("SLit", "Universal Render Pipeline/Lit"); g.unlit = MatAsset("SUnlit", "Universal Render Pipeline/Unlit");
            go.AddComponent<StringsBoot>();
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("[Strings] scene saved: " + ScenePath + "; hand skeleton type overrides in file: " + (File.ReadAllText(ScenePath).Split(new[] { "propertyPath: _skeletonType" }, System.StringSplitOptions.None).Length - 1));
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        [MenuItem("Strings/Play Autopilot (visible)")]
        public static void Autopilot()
        {
            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.isPlaying = true;
        }
    }
}
