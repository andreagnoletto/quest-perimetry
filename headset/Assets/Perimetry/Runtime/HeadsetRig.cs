using Perimetry.Core.Exam;
using Perimetry.Core.Geometry;
using Perimetry.Headset.Logic;
using UnityEngine;

namespace Perimetry.Headset
{
    /// <summary>
    /// Head-locked scene built at runtime as children of the camera: a background shell, the stimulus disc,
    /// the yellow fixation diamond and the message text. Every frame it draws exactly the
    /// <see cref="FlowView"/> it is given: geometry in angular coordinates around the tested eye
    /// (<see cref="StimulusPlacement"/>), luminance through <see cref="LuminanceMapper"/> per eye, eye routing
    /// and dithering in the Perimetry/MonocularUnlit shader.
    /// </summary>
    public sealed class HeadsetRig
    {
        // Luminance of sRGB yellow (1,1,0) relative to white (Rec. 709 weights): drive R=G for the target luminance.
        private const double YellowRelativeLuminance = 0.2126 + 0.7152;

        private static readonly int LevelLeftId = Shader.PropertyToID("_LevelLeft");
        private static readonly int LevelRightId = Shader.PropertyToID("_LevelRight");
        private static readonly int EyeId = Shader.PropertyToID("_PerimetryEye");
        private static readonly int UntestedId = Shader.PropertyToID("_PerimetryUntestedLevel");
        private static readonly int MaxCodeId = Shader.PropertyToID("_PerimetryMaxCode");
        private static readonly int DitherId = Shader.PropertyToID("_PerimetryDither");
        private static readonly int FrameId = Shader.PropertyToID("_PerimetryFrame");

        private readonly Camera _camera;
        private readonly HeadTracking _head;
        private readonly LuminanceMapper _lum;
        private readonly HeadsetConfig _h;
        private readonly double _distanceM;

        private readonly Transform _root;
        private readonly Material _bgMat, _stimMat, _fixMat, _textMat;
        private readonly Transform _stim;
        private readonly TextMesh _text;
        private readonly int _maxCharsPerLine;
        private string _lastMessage;
        private readonly Renderer _stimRenderer, _textRenderer;
        // Fixation target: one diamond in mode A, four dots for the foveal Diamond4 target (Phase 5).
        private readonly Transform[] _fix = new Transform[4];
        private readonly Renderer[] _fixRenderer = new Renderer[4];
        private int _frame;

        public HeadsetRig(Camera camera, HeadTracking head, LuminanceMapper lum, HeadsetConfig headset, double stimulusDistanceM)
        {
            _camera = camera; _head = head; _lum = lum; _h = headset; _distanceM = stimulusDistanceM;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = (float)headset.Presentation.BackgroundShellRadiusM * 2;
            camera.allowHDR = false;
            camera.allowMSAA = false;

            var unlit = Shader.Find("Perimetry/MonocularUnlit") ?? throw new System.InvalidOperationException("shader Perimetry/MonocularUnlit not found");
            var textShader = Shader.Find("Perimetry/MonocularText") ?? throw new System.InvalidOperationException("shader Perimetry/MonocularText not found");
            _bgMat = new Material(unlit) { name = "Background" };
            _stimMat = new Material(unlit) { name = "Stimulus" };
            _fixMat = new Material(unlit) { name = "Fixation" };

            _root = new GameObject("PerimetryRig").transform;
            _root.SetParent(camera.transform, false);

            var shell = MakeObject("BackgroundShell", MeshFactory.InvertedSphere(48, 24), _bgMat);
            shell.localScale = Vector3.one * (float)headset.Presentation.BackgroundShellRadiusM;

            _stim = MakeObject("StimulusDisc", MeshFactory.Disc(64), _stimMat);
            _stimRenderer = _stim.GetComponent<Renderer>();
            var diamond = MeshFactory.Diamond();
            for (int i = 0; i < _fix.Length; i++)
            {
                _fix[i] = MakeObject(i == 0 ? "FixationDiamond" : "FixationDot" + i, diamond, _fixMat);
                _fixRenderer[i] = _fix[i].GetComponent<Renderer>();
                _fixRenderer[i].enabled = false;
            }

            var textGo = new GameObject("Message");
            textGo.transform.SetParent(_root, false);
            _text = textGo.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _text.font = font;
            _text.fontSize = 64;
            _text.characterSize = (float)MessageLayout.CharacterSize(
                headset.Presentation.MessageLineHeightDeg, headset.Presentation.MessageDistanceM, _text.fontSize);
            _maxCharsPerLine = MessageLayout.MaxCharsPerLine(headset.Presentation.MessageLineHeightDeg, headset.Presentation.MessageMaxWidthDeg);
            _text.anchor = TextAnchor.UpperCenter;
            _text.alignment = TextAlignment.Center;
            _text.lineSpacing = 1.1f;
            _textRenderer = textGo.GetComponent<MeshRenderer>();
            _textMat = new Material(textShader) { name = "Message", mainTexture = font.material.mainTexture };
            _textRenderer.sharedMaterial = _textMat;
            double a = headset.Presentation.MessageOffsetDeg * System.Math.PI / 180, dm = headset.Presentation.MessageDistanceM;
            textGo.transform.localPosition = new Vector3(0, (float)(System.Math.Sin(a) * dm), (float)(System.Math.Cos(a) * dm));

            Shader.SetGlobalFloat(MaxCodeId, (1 << lum.Display.PanelBitsPerChannel) - 1);
            Shader.SetGlobalFloat(DitherId, (int)lum.DitherMode);
        }

        public Transform Root => _root;
        public Transform StimulusTransform => _stim;
        public bool StimulusVisible => _stimRenderer.enabled;
        /// <summary>The central fixation diamond (mode A) or the first dot of the Diamond4 target.</summary>
        public Transform FixationTransform => _fix[0];
        /// <summary>Number of fixation points drawn this frame (0, 1 or 4).</summary>
        public int FixationPointsVisible
        {
            get { int n = 0; foreach (var r in _fixRenderer) if (r.enabled) n++; return n; }
        }

        private Transform MakeObject(string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go.transform;
        }

        private Vector4 Grey(Eye eye, double cdm2)
        {
            float l = (float)_lum.LevelFor(eye, cdm2);
            return new Vector4(l, l, l, 1);
        }

        private void SetGrey(Material m, double cdm2)
        {
            m.SetVector(LevelLeftId, Grey(Eye.OS, cdm2));
            m.SetVector(LevelRightId, Grey(Eye.OD, cdm2));
        }

        private Vector3 EyeLocal(Eye? eye)
        {
            if (eye == Eye.OD) return _head.RightEyeLocal;
            if (eye == Eye.OS) return _head.LeftEyeLocal;
            return (_head.LeftEyeLocal + _head.RightEyeLocal) / 2;
        }

        /// <summary>Places a disc of <paramref name="diameterDeg"/> at field position (x, y) seen from <paramref name="eye"/>.</summary>
        public void Place(Transform t, double fieldX, double fieldY, double diameterDeg, Eye? eye, double distanceM)
        {
            var e = EyeLocal(eye);
            var p = StimulusPlacement.Place(fieldX, fieldY, diameterDeg, (e.x, e.y, e.z), distanceM);
            t.localPosition = new Vector3((float)p.X, (float)p.Y, (float)p.Z);
            // disc mesh faces -Z: point its +Z away from the eye, along the line of sight
            t.localRotation = Quaternion.LookRotation(new Vector3((float)p.DirX, (float)p.DirY, (float)p.DirZ), Vector3.up);
            t.localScale = new Vector3((float)p.RadiusM, (float)p.RadiusM, 1);
        }

        public void Render(FlowView v)
        {
            _head.UpdateEyes();
            _frame++;
            Shader.SetGlobalFloat(FrameId, _frame & 15);
            Shader.SetGlobalFloat(EyeId, v.MonocularEye == null ? -1 : v.MonocularEye == Eye.OD ? 1 : 0);
            Shader.SetGlobalFloat(UntestedId, (float)_lum.LevelFor(v.MonocularEye == Eye.OD ? Eye.OS : Eye.OD, v.UntestedEyeCdm2));

            SetGrey(_bgMat, v.BackgroundCdm2);

            _stimRenderer.enabled = v.Stimulus != null;
            if (v.Stimulus != null)
            {
                Place(_stim, v.Stimulus.FieldX, v.Stimulus.FieldY, v.Stimulus.DiameterDeg, v.Stimulus.Eye, _distanceM);
                _stimMat.SetVector(v.Stimulus.Eye == Eye.OD ? LevelRightId : LevelLeftId, Grey(v.Stimulus.Eye, v.Stimulus.LuminanceCdm2));
                _stimMat.SetVector(v.Stimulus.Eye == Eye.OD ? LevelLeftId : LevelRightId, Grey(v.Stimulus.Eye == Eye.OD ? Eye.OS : Eye.OD, v.BackgroundCdm2));
            }

            // Fixation target (Phase 5: kind, position in mode B, lock animation scale/luminance, blink marker).
            var points = v.ShowFixation
                ? FixationLayout.Points(v.FixationTarget, new FieldPoint(v.FixationX, v.FixationY), v.DiamondHalfSizeDeg)
                : null;
            for (int i = 0; i < _fix.Length; i++)
            {
                bool on = points != null && i < points.Count;
                _fixRenderer[i].enabled = on;
                if (!on) continue;
                double size = v.FixationTarget == FixationTargetKind.Diamond4 ? v.DiamondDotSizeDeg : v.FixationSizeDeg * v.FixationScale;
                // slightly nearer than the stimulus sphere so it is never hidden by a disc
                Place(_fix[i], points[i].X, points[i].Y, size, v.MonocularEye, _distanceM * 0.98);
            }
            if (points != null)
            {
                double white = v.FixationLuminanceCdm2 * v.FixationLuminanceFactor / YellowRelativeLuminance;
                foreach (var (id, eye) in new[] { (LevelLeftId, Eye.OS), (LevelRightId, Eye.OD) })
                {
                    float l = (float)_lum.LevelFor(eye, white);
                    _fixMat.SetVector(id, new Vector4(l, l, 0, 1));
                }
            }

            _textRenderer.enabled = !string.IsNullOrEmpty(v.Message);
            if (_textRenderer.enabled)
            {
                if (v.Message != _lastMessage)
                {
                    _lastMessage = v.Message;
                    _text.text = MessageLayout.Wrap(v.Message, _maxCharsPerLine);
                }
                SetGrey(_textMat, _h.Presentation.TextLuminanceCdm2);
            }
        }
    }

    /// <summary>Meshes built from <see cref="DiscMesh"/> and simple primitives.</summary>
    public static class MeshFactory
    {
        public static Mesh Disc(int segments)
        {
            var (xy, tris) = DiscMesh.Build(segments);
            var v = new Vector3[xy.Length / 2];
            for (int i = 0; i < v.Length; i++) v[i] = new Vector3((float)xy[i * 2], (float)xy[i * 2 + 1], 0);
            var m = new Mesh { name = "Disc" + segments, vertices = v, triangles = tris };
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Diamond (square rotated 45°) whose tip-to-tip size is 2 units, i.e. scale = half the size.</summary>
        public static Mesh Diamond()
        {
            var m = new Mesh
            {
                name = "Diamond",
                vertices = new[] { new Vector3(0, 1, 0), new Vector3(1, 0, 0), new Vector3(0, -1, 0), new Vector3(-1, 0, 0) },
                triangles = new[] { 0, 1, 2, 0, 2, 3 },
            };
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Unit sphere with inward-facing triangles (we are inside it).</summary>
        public static Mesh InvertedSphere(int lon, int lat)
        {
            var verts = new Vector3[(lon + 1) * (lat + 1)];
            for (int j = 0; j <= lat; j++)
            {
                float th = Mathf.PI * j / lat;
                for (int i = 0; i <= lon; i++)
                {
                    float ph = 2 * Mathf.PI * i / lon;
                    verts[j * (lon + 1) + i] = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                }
            }
            var tris = new int[lon * lat * 6];
            int k = 0;
            for (int j = 0; j < lat; j++)
                for (int i = 0; i < lon; i++)
                {
                    int a = j * (lon + 1) + i, b = a + lon + 1;
                    tris[k++] = a; tris[k++] = a + 1; tris[k++] = b;
                    tris[k++] = a + 1; tris[k++] = b + 1; tris[k++] = b;
                }
            var m = new Mesh { name = "InvertedSphere", vertices = verts, triangles = tris };
            m.RecalculateBounds();
            return m;
        }
    }
}
