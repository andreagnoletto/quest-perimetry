using System.IO;
using System.Xml;
using UnityEditor.Android;
using UnityEngine;

namespace Perimetry.Headset.Editor
{
    /// <summary>
    /// Adds the Meta eye-tracking permission and the optional eye-tracking feature to the generated Android
    /// manifest (Phase 5). The permission is requested at runtime only on a device that has an eye tracker
    /// (<see cref="GazeTracking"/>); on the Quest 3 nothing is asked and the exam runs as before.
    /// </summary>
    public sealed class AndroidManifestPatch : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 10;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string manifest = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifest)) { Debug.LogWarning("[Perimetry] AndroidManifest.xml not found at " + manifest); return; }
            var doc = new XmlDocument();
            doc.Load(manifest);
            var root = doc.DocumentElement;
            if (root == null) return;
            const string android = "http://schemas.android.com/apk/res/android";
            bool changed = false;
            changed |= Ensure(doc, root, "uses-permission", android, GazeTracking.AndroidPermission, null);
            changed |= Ensure(doc, root, "uses-feature", android, "oculus.software.eye_tracking", "false");
            if (changed) doc.Save(manifest);
            Debug.Log("[Perimetry] manifest: eye-tracking permission and optional feature " + (changed ? "added" : "already present"));
        }

        private static bool Ensure(XmlDocument doc, XmlElement root, string tag, string ns, string name, string required)
        {
            foreach (XmlNode n in root.GetElementsByTagName(tag))
                if (n.Attributes?["android:name"]?.Value == name) return false;
            var e = doc.CreateElement(tag);
            var a = doc.CreateAttribute("android", "name", ns);
            a.Value = name;
            e.Attributes.Append(a);
            if (required != null)
            {
                var r = doc.CreateAttribute("android", "required", ns);
                r.Value = required;
                e.Attributes.Append(r);
            }
            root.AppendChild(e);
            return true;
        }
    }
}
