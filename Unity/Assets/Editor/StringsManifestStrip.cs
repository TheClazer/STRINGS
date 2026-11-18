#if UNITY_ANDROID
using System.IO;
using System.Xml;
using UnityEditor;
using UnityEditor.Android;
using UnityEngine;

namespace Strings.Editor
{
    /// <summary>
    /// STRINGS only (PlayerSettings.productName == "STRINGS"): the shared RoomIsLying_v2 project pulls in permissions STRINGS never
    /// uses (microphone, audio settings, Bluetooth, spatial anchors) and the Bluetooth hardware feature. After Unity and the Meta SDK
    /// have written the Gradle project, this removes those nodes from the unityLibrary and launcher manifests and adds
    /// tools:node="remove" markers to both, so the Gradle manifest merger also drops them if a plugin library declares them.
    /// HAND_TRACKING and every other entry are left alone.
    /// </summary>
    public class StringsManifestStrip : IPostGenerateGradleAndroidProject
    {
        const string AndroidNs = "http://schemas.android.com/apk/res/android";
        const string ToolsNs = "http://schemas.android.com/tools";
        static readonly string[] Permissions =
        {
            "android.permission.RECORD_AUDIO", "android.permission.MODIFY_AUDIO_SETTINGS", "android.permission.BLUETOOTH",
            "com.oculus.permission.USE_ANCHOR_API",
        };
        static readonly string[] Features = { "android.hardware.bluetooth" };

        public int callbackOrder => 1000;     // after OVRGradleGeneration (3) and the Meta manifest processors (10)

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            if (PlayerSettings.productName != "STRINGS") return;
            string root = Path.GetDirectoryName(path.TrimEnd('/', '\\'));
            foreach (var m in new[] { Path.Combine(path, "src", "main", "AndroidManifest.xml"), Path.Combine(root, "launcher", "src", "main", "AndroidManifest.xml") })
            {
                if (!File.Exists(m)) { Debug.LogWarning("[STRINGS] manifest strip: not found " + m); continue; }
                try { Strip(m); }
                catch (System.Exception e) { Debug.LogError("[STRINGS] manifest strip failed on " + m + ": " + e.Message); }
            }
        }

        static void Strip(string file)
        {
            var doc = new XmlDocument(); doc.PreserveWhitespace = false; doc.Load(file);
            var manifest = doc.DocumentElement; if (manifest == null || manifest.Name != "manifest") return;
            if (!manifest.HasAttribute("xmlns:tools")) manifest.SetAttribute("xmlns:tools", ToolsNs);
            int removed = 0, marked = 0;
            removed += RemoveAll(manifest, "uses-permission", Permissions);
            removed += RemoveAll(manifest, "uses-permission-sdk-23", Permissions);
            removed += RemoveAll(manifest, "uses-feature", Features);
            foreach (var p in Permissions) { AddRemoveMarker(doc, manifest, "uses-permission", p); marked++; }
            foreach (var f in Features) { AddRemoveMarker(doc, manifest, "uses-feature", f); marked++; }
            var settings = new XmlWriterSettings { Indent = true, NewLineChars = "\n" };
            using (var w = XmlWriter.Create(file, settings)) doc.Save(w);
            Debug.Log("[STRINGS] manifest strip: " + file + " removed " + removed + " node(s), added " + marked + " remove marker(s)");
        }

        static int RemoveAll(XmlElement manifest, string tag, string[] names)
        {
            int n = 0;
            var list = new System.Collections.Generic.List<XmlNode>();
            foreach (XmlNode c in manifest.ChildNodes) if (c.Name == tag) list.Add(c);
            foreach (XmlNode node in list)
            {
                var e = node as XmlElement; if (e == null) continue;
                string nm = e.GetAttribute("name", AndroidNs);
                if (System.Array.IndexOf(names, nm) >= 0) { manifest.RemoveChild(e); n++; }
            }
            return n;
        }

        static void AddRemoveMarker(XmlDocument doc, XmlElement manifest, string tag, string name)
        {
            foreach (XmlNode c in manifest.ChildNodes) if (c is XmlElement x && x.Name == tag && x.GetAttribute("name", AndroidNs) == name) return;   // already marked (incremental export)
            var e = doc.CreateElement(tag);
            var an = doc.CreateAttribute("android", "name", AndroidNs); an.Value = name; e.Attributes.Append(an);
            var tn = doc.CreateAttribute("tools", "node", ToolsNs); tn.Value = "remove"; e.Attributes.Append(tn);
            manifest.AppendChild(e);
        }
    }
}
#endif
