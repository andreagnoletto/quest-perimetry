using System.IO;
using NUnit.Framework;
using Perimetry.Core.Config;
using Perimetry.Core.Geometry;
using Perimetry.Headset.Logic;
using UnityEngine;

namespace Perimetry.Headset.Tests
{
    /// <summary>
    /// Unity-side checks of what the patient actually sees: the real <see cref="HeadsetRig"/> objects
    /// (transforms + mesh vertices, after Unity's float maths) measured from the tested eye.
    /// The pure geometry is also tested outside Unity in headset/LogicTests.
    /// </summary>
    public class RigTests
    {
        private GameObject _camGo;
        private HeadsetRig _rig;
        private HeadTracking _head;
        private ClinicalConfig _cfg;

        [SetUp]
        public void SetUp()
        {
            _cfg = ConfigLoader.Load();
            var h = HeadsetConfig.From(_cfg);
            _camGo = new GameObject("TestCamera");
            var cam = _camGo.AddComponent<Camera>();
            // tilt the head so the test does not depend on an identity pose
            _camGo.transform.SetPositionAndRotation(new Vector3(0.2f, 1.6f, -0.3f), Quaternion.Euler(8, -25, 3));
            _head = new HeadTracking(cam, 0.063f);
            _rig = new HeadsetRig(cam, _head, new LuminanceMapper(_cfg.Display), h, _cfg.Display.VirtualStimulusDistanceM);
        }

        [TearDown]
        public void TearDown()
        {
            _head.Dispose();
            Object.DestroyImmediate(_camGo);
        }

        private FlowView ViewWith(Eye eye, double x, double y, double deg) => new FlowView
        {
            State = FlowState.Exam, MonocularEye = eye, BackgroundCdm2 = 10, ShowFixation = true, FixationLuminanceCdm2 = 30, FixationSizeDeg = 0.8,
            Stimulus = new StimulusDraw { Eye = eye, FieldX = x, FieldY = y, DiameterDeg = deg, LuminanceCdm2 = 20 },
        };

        /// <summary>SPEC §2 / §11: stimulus angular size verified automatically, error &lt; 5 %.</summary>
        [TestCase(Eye.OD, 27, 3, GoldmannSize.III)]
        [TestCase(Eye.OD, -21, -21, GoldmannSize.III)]
        [TestCase(Eye.OS, -27, 3, GoldmannSize.III)]
        [TestCase(Eye.OS, 3, 3, GoldmannSize.I)]
        [TestCase(Eye.OD, 9, -27, GoldmannSize.V)]
        public void RenderedStimulus_SubtendsGoldmannSize_FromTestedEye(Eye eye, double x, double y, GoldmannSize size)
        {
            double want = Perimetry.Core.Geometry.Stimulus.DiameterDeg(_cfg.Stimulus, size);
            _rig.Render(ViewWith(eye, x, y, want));
            Assert.That(_rig.StimulusVisible);

            var t = _rig.StimulusTransform;
            var mesh = t.GetComponent<MeshFilter>().sharedMesh;
            var cam = _camGo.transform;
            Vector3 eyeWorld = cam.TransformPoint(eye == Eye.OD ? _head.RightEyeLocal : _head.LeftEyeLocal);
            Assert.That(_head.EyeSource, Is.EqualTo("fallback"), "no XR in EditMode");

            // diameter = largest angle between opposite rim vertices seen from the eye (area-equivalent polygon)
            var v = mesh.vertices;
            int n = v.Length - 1;
            double maxAngle = 0, minAngle = 180;
            for (int i = 1; i <= n / 2; i++)
            {
                Vector3 a = t.TransformPoint(v[i]) - eyeWorld, b = t.TransformPoint(v[i + n / 2]) - eyeWorld;
                double ang = Vector3.Angle(a, b);
                maxAngle = System.Math.Max(maxAngle, ang); minAngle = System.Math.Min(minAngle, ang);
            }
            double measured = (maxAngle + minAngle) / 2;
            Assert.That(System.Math.Abs(measured - want) / want, Is.LessThan(0.05), $"measured {measured:F4}° want {want:F4}°");

            // and it is where the field coordinate says, relative to the tested eye's line of sight
            Vector3 centreDir = cam.InverseTransformDirection(t.position - eyeWorld).normalized;
            var expected = Perimetry.Core.Geometry.Stimulus.Direction(new FieldPoint(x, y));
            Assert.That(Vector3.Angle(centreDir, new Vector3((float)expected.x, (float)expected.y, (float)expected.z)), Is.LessThan(0.01f));
        }

        [Test]
        public void StimulusHidden_WhenViewHasNone()
        {
            var v = ViewWith(Eye.OD, 9, 9, 0.43);
            v.Stimulus = null;
            _rig.Render(v);
            Assert.That(_rig.StimulusVisible, Is.False);
        }

        [Test]
        public void ResourcesConfig_HasSameHashAsRepositoryConfig()
        {
            var repo = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "config"));
            Assert.That(_cfg.Hash, Is.EqualTo(ClinicalConfig.LoadFromDirectory(repo).Hash), "run Perimetry > Sync Config");
        }

        [Test]
        public void Shaders_AreIncluded()
        {
            Assert.That(Shader.Find("Perimetry/MonocularUnlit"), Is.Not.Null);
            Assert.That(Shader.Find("Perimetry/MonocularText"), Is.Not.Null);
        }
    }
}
