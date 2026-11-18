using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Strings.Editor
{
    public static class StringsBuildApk
    {
        const string ScenePath = "Assets/Strings/Strings_Main.unity";
        const string OutDir = "E:/Winter/metavr/Builds";
        const string AppId = "com.roomislying.strings";

        [MenuItem("Strings/Pre-flight check")]
        public static void PreFlight()
        {
            int problems = RunChecks();
            Debug.Log("[STRINGS] PreFlight finished, warnings/problems: " + problems);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        [MenuItem("Strings/Build Quest APK")]
        public static void BuildStrings()
        {
            RunChecks();

            PlayerSettings.productName = "STRINGS";
            // every upload needs a higher version code; 3 = the Nov 17 candidate (gesture decoder, first fight before the Workshop, manifest strip)
            const int VersionCode = 3;
            if (PlayerSettings.Android.bundleVersionCode < VersionCode) PlayerSettings.Android.bundleVersionCode = VersionCode;
            Debug.Log("[STRINGS] version code " + PlayerSettings.Android.bundleVersionCode);
            var target = NamedBuildTarget.Android;
            string id = PlayerSettings.GetApplicationIdentifier(target);
            if (id != AppId)
            {
                Debug.Log("[STRINGS] applicationIdentifier " + id + " -> " + AppId);
                PlayerSettings.SetApplicationIdentifier(target, AppId);
            }
            PlayerSettings.SetScriptingBackend(target, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

            if (!File.Exists(ScenePath))
            {
                Debug.LogError("[STRINGS] Scene missing: " + ScenePath);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            Directory.CreateDirectory(OutDir);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmm");
            string apk = OutDir + "/STRINGS_" + stamp + (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-str-dev") >= 0 ? "_dev" : "") + ".apk";

            var opts = new BuildPlayerOptions();
            opts.scenes = new[] { ScenePath };
            opts.locationPathName = apk;
            opts.target = BuildTarget.Android;
            opts.options = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-str-dev") >= 0 ? (BuildOptions.Development | BuildOptions.AllowDebugging) : BuildOptions.None;   // -str-dev: development build for on-device crash logs

            BuildReport report = BuildPipeline.BuildPlayer(opts);
            var s = report.summary;
            Debug.Log("[STRINGS] APK build result: " + s.result + ", size " + (s.totalSize / (1024 * 1024)) + " MB, errors " + s.totalErrors + ", path " + apk);
            if (Application.isBatchMode) EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
        }

        static int RunChecks()
        {
            int bad = 0;
            Debug.Log("[STRINGS] ---- PreFlight checklist ----");

            if (File.Exists(ScenePath)) Debug.Log("[STRINGS] OK scene present: " + ScenePath);
            else { Debug.LogWarning("[STRINGS] scene MISSING: " + ScenePath); bad++; }

            var target = NamedBuildTarget.Android;
            var backend = PlayerSettings.GetScriptingBackend(target);
            if (backend == ScriptingImplementation.IL2CPP) Debug.Log("[STRINGS] OK scripting backend IL2CPP");
            else { Debug.LogWarning("[STRINGS] scripting backend is " + backend + " (BuildStrings sets IL2CPP)"); bad++; }

            var arch = PlayerSettings.Android.targetArchitectures;
            if (arch == AndroidArchitecture.ARM64) Debug.Log("[STRINGS] OK architecture ARM64 only");
            else { Debug.LogWarning("[STRINGS] architectures are " + arch + " (BuildStrings sets ARM64)"); bad++; }

            var minSdk = PlayerSettings.Android.minSdkVersion;
            if ((int)minSdk >= 29) Debug.Log("[STRINGS] OK min API " + minSdk);
            else { Debug.LogWarning("[STRINGS] min API " + minSdk + " is low for Quest (use 29+)"); bad++; }

            Debug.Log("[STRINGS] target API " + PlayerSettings.Android.targetSdkVersion + " (Auto = highest installed)");
            Debug.Log("[STRINGS] applicationIdentifier " + PlayerSettings.GetApplicationIdentifier(target) + ", product '" + PlayerSettings.productName + "'");

            var sub = EditorUserBuildSettings.androidBuildSubtarget;
            if (sub == MobileTextureSubtarget.ASTC || sub == MobileTextureSubtarget.Generic) Debug.Log("[STRINGS] OK texture compression " + sub + " (Generic = ASTC default)");
            else { Debug.LogWarning("[STRINGS] texture compression is " + sub + ", set ASTC"); bad++; }

            if (PlayerSettings.Android.forceInternetPermission) { Debug.LogWarning("[STRINGS] forceInternetPermission is ON, not needed"); bad++; }
            else Debug.Log("[STRINGS] OK internet permission not forced (Unity may still add it if scripts use networking)");

            var cfg = OVRProjectConfig.CachedProjectConfig;
            if (cfg == null) { Debug.LogWarning("[STRINGS] OVRProjectConfig not found"); bad++; }
            else
            {
                if (cfg.handTrackingSupport != OVRProjectConfig.HandTrackingSupport.HandsOnly)
                {
                    Debug.LogWarning("[STRINGS] hand tracking was " + cfg.handTrackingSupport + ", setting HandsOnly");
                    cfg.handTrackingSupport = OVRProjectConfig.HandTrackingSupport.HandsOnly;
                    OVRProjectConfig.CommitProjectConfig(cfg);
                }
                else Debug.Log("[STRINGS] OK hand tracking HandsOnly, frequency " + cfg.handTrackingFrequency);

                // STRINGS is fully virtual: asking for room (scene) data or passthrough only adds a permission prompt on first launch
                if (cfg.sceneSupport != OVRProjectConfig.FeatureSupport.None || cfg.insightPassthroughSupport != OVRProjectConfig.FeatureSupport.None)
                {
                    Debug.LogWarning("[STRINGS] sceneSupport=" + cfg.sceneSupport + ", passthrough=" + cfg.insightPassthroughSupport + ": setting both to None (STRINGS uses neither)");
                    cfg.sceneSupport = OVRProjectConfig.FeatureSupport.None; cfg.insightPassthroughSupport = OVRProjectConfig.FeatureSupport.None;
                    OVRProjectConfig.CommitProjectConfig(cfg);
                }

                if (cfg.bodyTrackingSupport != OVRProjectConfig.FeatureSupport.None || cfg.faceTrackingSupport != OVRProjectConfig.FeatureSupport.None || cfg.eyeTrackingSupport != OVRProjectConfig.FeatureSupport.None)
                { Debug.LogWarning("[STRINGS] body/face/eye tracking requested; adds permission prompts"); bad++; }
                if (cfg.sceneSupport != OVRProjectConfig.FeatureSupport.None)
                { Debug.LogWarning("[STRINGS] sceneSupport=" + cfg.sceneSupport + " adds the Spatial Data permission prompt; turn off if STRINGS does not use the room"); bad++; }
                if (cfg.insightPassthroughSupport != OVRProjectConfig.FeatureSupport.None)
                    Debug.Log("[STRINGS] passthrough support = " + cfg.insightPassthroughSupport + " (needed only if the scene uses passthrough)");
            }

            Debug.Log("[STRINGS] RECORD_AUDIO, MODIFY_AUDIO_SETTINGS, BLUETOOTH, USE_ANCHOR_API and the Bluetooth feature are stripped by StringsManifestStrip; confirm with aapt dump badging.");
            return bad;
        }
    }
}
