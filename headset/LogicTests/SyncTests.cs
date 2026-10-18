using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Perimetry.Core.Geometry;
using Perimetry.Headset.Logic;

namespace Perimetry.Headset.Logic.Tests
{
    /// <summary>In-memory file system for <see cref="PortalSync"/>.</summary>
    internal sealed class FakeFiles : ISyncFiles
    {
        public readonly Dictionary<string, string> Files = new Dictionary<string, string>(StringComparer.Ordinal);
        public bool Exists(string path) => Files.ContainsKey(path);
        public string ReadText(string path) => Files[path];
        public void WriteText(string path, string text) => Files[path] = text;
        public IReadOnlyList<string> List(string directory)
        {
            string prefix = directory.TrimEnd('/') + "/";
            return Files.Keys.Where(k => k.StartsWith(prefix) && !k.Substring(prefix.Length).Contains("/"))
                .Select(k => k.Substring(prefix.Length)).OrderBy(n => n, StringComparer.Ordinal).ToList();
        }
        public void Move(string from, string to) { Files[to] = Files[from]; Files.Remove(from); }
    }

    /// <summary>
    /// Scripted portal: answers each request by purpose. Requests are answered on the next frame (like a fast
    /// LAN) unless <see cref="Offline"/>, in which case they fail.
    /// </summary>
    internal sealed class FakePortal
    {
        public readonly List<HttpCall> Sent = new List<HttpCall>();
        public readonly Dictionary<string, Func<HttpCall, (int, string)>> Handlers = new Dictionary<string, Func<HttpCall, (int, string)>>();
        public readonly List<JObject> LiveBodies = new List<JObject>();
        public readonly Queue<JObject> Commands = new Queue<JObject>();
        public bool Offline;
        private readonly List<HttpCall> _pending = new List<HttpCall>();

        public FakePortal()
        {
            Handlers["pair"] = c => (200, new JObject { ["schema"] = PortalSync.Schema, ["deviceId"] = "dev1", ["token"] = "tok-123", ["portalName"] = "Portal Teste" }.ToString());
            Handlers["orders"] = c => (200, new JObject { ["schema"] = PortalSync.Schema, ["orders"] = new JArray() }.ToString());
            Handlers["upload"] = c =>
            {
                var results = new JArray();
                foreach (var f in (JArray)JObject.Parse(c.JsonBody)["files"])
                    results.Add(new JObject { ["name"] = f["name"], ["status"] = "imported", ["message"] = "ok", ["examDbId"] = "db-" + f["name"], ["integrity"] = "verified" });
                return (200, new JObject { ["schema"] = PortalSync.Schema, ["results"] = results }.ToString());
            };
            Handlers["live"] = c =>
            {
                LiveBodies.Add(JObject.Parse(c.JsonBody));
                var cmds = new JArray();
                while (Commands.Count > 0) cmds.Add(Commands.Dequeue());
                return (200, new JObject { ["schema"] = PortalSync.Schema, ["commands"] = cmds }.ToString());
            };
        }

        public void Frame(PortalSync sync, double now, PatientFlow flow)
        {
            // answer last frame's requests first, then collect new ones
            foreach (var c in _pending)
            {
                if (Offline) { sync.Fail(c.Id, "connection refused"); continue; }
                var (status, body) = Handlers[c.Purpose](c);
                sync.Complete(c.Id, status, body);
            }
            _pending.Clear();
            var calls = sync.Tick(now, flow);
            Sent.AddRange(calls);
            _pending.AddRange(calls);
        }

        public int Count(string purpose) => Sent.Count(c => c.Purpose == purpose);
    }

    public class SyncTests
    {
        private const string Data = "/data";
        private static readonly SyncPaths Paths = new SyncPaths(Data);
        private static readonly SyncIdentity Identity = new SyncIdentity { Serial = "Q3-TEST", Model = "Quest 3", Software = "test", HmacKeyBase64 = Convert.ToBase64String(new byte[32]) };

        private static (PortalSync sync, FakeFiles files, FakePortal portal) NewSync(bool withCode = true, bool withToken = false)
        {
            var files = new FakeFiles();
            if (withCode) files.Files[Paths.Portal] = "{\"url\":\"http://portal.local:3000/\",\"pairingCode\":\"7K3M-9Q2X\"}";
            if (withToken) files.Files[Paths.Token] = "{\"url\":\"http://portal.local:3000\",\"deviceId\":\"dev1\",\"token\":\"tok-old\",\"pairedUtc\":\"2026-10-18T00:00:00Z\"}";
            var sync = new PortalSync(Env.Config, Env.Headset, Paths, files, Identity, () => new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc));
            return (sync, files, new FakePortal());
        }

        private static PatientFlow ManualFlow(MemorySink sink = null) =>
            new PatientFlow(Env.Config, Env.Headset, Env.Lum, sink ?? new MemorySink(), new ExamOrder { Seed = 3 });

        private static JObject Order(string id, string code, string preset, double age, long? seed = null) =>
            new JObject { ["id"] = id, ["patientRef"] = code, ["ageYears"] = age, ["refractiveCorrection"] = "+1,00 esf", ["presetId"] = preset, ["seed"] = seed.HasValue ? (JToken)seed.Value : JValue.CreateNull(), ["createdUtc"] = "2026-10-18T11:00:00Z" };

        /// <summary>Runs sync + flow together, frame by frame, until the condition holds.</summary>
        private static void Run(FakePortal portal, PortalSync sync, FlowDriver d, Func<bool> cond, double maxMs = 30 * 60 * 1000)
        {
            double end = d.Now + maxMs;
            while (!cond())
            {
                if (d.Now > end) Assert.Fail($"timed out in state {d.Flow.State}, sync {sync.Status}");
                d.Frame();
                portal.Frame(sync, d.Now, d.Flow);
            }
        }

        [Test]
        public void Pairing_WritesToken_RenamesPortalFile_ThenFetchesOrders()
        {
            var (sync, files, portal) = NewSync();
            var flow = ManualFlow();
            var d = new FlowDriver(flow, 1);
            Run(portal, sync, d, () => sync.IsPaired && portal.Count("orders") >= 1, 5000);

            var pair = portal.Sent.First(c => c.Purpose == "pair");
            Assert.That(pair.Method, Is.EqualTo("POST"));
            Assert.That(pair.Url, Is.EqualTo("http://portal.local:3000/api/headset/pair"));
            Assert.That(pair.BearerToken, Is.Null);
            var body = JObject.Parse(pair.JsonBody);
            Assert.That((string)body["code"], Is.EqualTo("7K3M-9Q2X"));
            Assert.That((string)body["serial"], Is.EqualTo("Q3-TEST"));
            Assert.That((string)body["hmacKey"], Is.EqualTo(Identity.HmacKeyBase64));
            Assert.That((string)body["schema"], Is.EqualTo("perimetry-sync/1"));

            Assert.That(files.Exists(Paths.Portal), Is.False, "portal.json consumed");
            Assert.That(files.Files.Keys.Any(k => k.StartsWith(Data + "/portal.used-")), "renamed, not deleted");
            var token = JObject.Parse(files.ReadText(Paths.Token));
            Assert.That((string)token["token"], Is.EqualTo("tok-123"));
            Assert.That((string)token["deviceId"], Is.EqualTo("dev1"));
            Assert.That((string)token["url"], Is.EqualTo("http://portal.local:3000/"));

            var orders = portal.Sent.First(c => c.Purpose == "orders");
            Assert.That(orders.Method, Is.EqualTo("GET"));
            Assert.That(orders.BearerToken, Is.EqualTo("tok-123"));
            Assert.That(orders.Url, Is.EqualTo("http://portal.local:3000/api/headset/orders"));
            Assert.That(sync.Status, Does.StartWith("sync: paired"));
        }

        [Test]
        public void RejectedPairingCode_StopsPairing_WithoutRetryStorm()
        {
            var (sync, files, portal) = NewSync();
            portal.Handlers["pair"] = c => (403, "{\"schema\":\"perimetry-sync/1\",\"error\":\"invalid_code\",\"message\":\"código inválido\"}");
            var flow = ManualFlow();
            var d = new FlowDriver(flow, 1);
            for (int i = 0; i < 900; i++) { d.Frame(); portal.Frame(sync, d.Now, flow); } // 10 s
            Assert.That(portal.Count("pair"), Is.EqualTo(1));
            Assert.That(sync.IsPaired, Is.False);
            Assert.That(files.Exists(Paths.Portal), "the file stays so the operator can fix the code");
            Assert.That(sync.Status, Does.Contain("rejected"));
        }

        [Test]
        public void NoPortalFile_SyncIdle_FlowUnchanged()
        {
            var (sync, _, portal) = NewSync(withCode: false);
            var sink = new MemorySink();
            var flow = new PatientFlow(Env.Config, Env.Headset, Env.Lum, sink, new ExamOrder { PresetId = "24-2-zest-od", AgeYears = 50, Seed = 12 });
            var d = new FlowDriver(flow, 12);
            Run(portal, sync, d, () => flow.IsFinished);
            Assert.That(portal.Sent, Is.Empty);
            Assert.That(flow.State, Is.EqualTo(FlowState.Done));
            Assert.That(sync.Status, Is.EqualTo("sync: no portal.json"));
            Assert.That(sink.Exams, Has.Count.EqualTo(1));
        }

        [Test]
        public void Orders_ShownOnSetupScreen_SelectedOrderSeedsTheExam()
        {
            var (sync, _, portal) = NewSync(withCode: false, withToken: true);
            portal.Handlers["orders"] = c => (200, new JObject
            {
                ["schema"] = PortalSync.Schema,
                ["orders"] = new JArray(Order("ord-1", "P-AAAAAA", "24-2-zest-od", 63, 77), Order("ord-2", "P-BBBBBB", "24-2-zest-fast", 40), Order("ord-3", "P-CCCCCC", "no-such-preset", 50)),
            }.ToString());
            var sink = new MemorySink();
            var flow = ManualFlow(sink);
            var d = new FlowDriver(flow, 2);
            // the orders arrive while the welcome screen is up (the driver taps after 600 ms)
            Run(portal, sync, d, () => flow.PendingOrders.Count > 0, 500);
            Assert.That(flow.PendingOrders.Select(o => o.Id), Is.EqualTo(new[] { "ord-1", "ord-2" }), "unknown preset dropped");
            Assert.That(sync.Orders, Has.Count.EqualTo(3));

            Run(portal, sync, d, () => flow.State == FlowState.SetupOrder, 3000);
            Assert.That(flow.View.Message, Does.Contain("P-AAAAAA · 24-2 ZEST só OD · 63 anos"));
            flow.OperatorAdjust(+1);
            Assert.That(flow.View.Message, Does.Contain("P-BBBBBB · 24-2 ZEST rápido · 40 anos"));
            flow.OperatorAdjust(+1);
            Assert.That(flow.View.Message, Does.Contain("sem pedido (manual)"));
            flow.OperatorAdjust(+1);
            Assert.That(flow.View.Message, Does.Contain("P-AAAAAA"), "wraps around");

            d.Press(1700); // hold = confirm
            Run(portal, sync, d, () => flow.State == FlowState.Instructions, 3000);
            Assert.That(flow.ActiveOrder.Id, Is.EqualTo("ord-1"));
            Assert.That(flow.Preset.Id, Is.EqualTo("24-2-zest-od"));
            Assert.That(flow.AgeYears, Is.EqualTo(63));
            Assert.That(flow.Log.Order.PatientRef, Is.EqualTo("P-AAAAAA"));

            Run(portal, sync, d, () => flow.IsFinished);
            var exam = sink.Exams.Single();
            Assert.That(exam.Settings.PatientRef, Is.EqualTo("P-AAAAAA"));
            Assert.That(exam.Settings.Eye, Is.EqualTo(Eye.OD));
            Assert.That(exam.Settings.AgeYears, Is.EqualTo(63));
            Assert.That(exam.Settings.RefractiveCorrection, Is.EqualTo("+1,00 esf"));
            Assert.That(exam.Settings.Seed, Is.EqualTo(77UL), "order seed used for the exam");
            Assert.That(portal.LiveBodies.Last()["orderId"].ToString(), Is.EqualTo("ord-1"));
        }

        [Test]
        public void ManualOption_OnOrderScreen_GoesToPresetSetup()
        {
            var (sync, _, portal) = NewSync(withCode: false, withToken: true);
            portal.Handlers["orders"] = c => (200, new JObject { ["schema"] = PortalSync.Schema, ["orders"] = new JArray(Order("ord-1", "P-AAAAAA", "24-2-zest-od", 63)) }.ToString());
            var flow = ManualFlow();
            var d = new FlowDriver(flow, 2);
            Run(portal, sync, d, () => flow.State == FlowState.SetupOrder, 3000);
            flow.OperatorAdjust(-1);
            Assert.That(flow.View.Message, Does.Contain("sem pedido (manual)"));
            d.Press(1700);
            Run(portal, sync, d, () => flow.State == FlowState.SetupPreset, 3000);
            Assert.That(flow.ActiveOrder.Id, Is.Null);
            Assert.That(flow.Log.Events.Any(e => e.Type == "order" && e.Detail == "manual"));
        }

        [Test]
        public void FileOrder_SkipsOrderScreen_EvenWithPendingOrders()
        {
            var (sync, _, portal) = NewSync(withCode: false, withToken: true);
            portal.Handlers["orders"] = c => (200, new JObject { ["schema"] = PortalSync.Schema, ["orders"] = new JArray(Order("ord-1", "P-AAAAAA", "24-2-zest-od", 63)) }.ToString());
            var flow = new PatientFlow(Env.Config, Env.Headset, Env.Lum, new MemorySink(), new ExamOrder { PresetId = "24-2-zest-od", AgeYears = 50, PatientRef = "P-FILE", Seed = 1 });
            var d = new FlowDriver(flow, 1);
            Run(portal, sync, d, () => flow.State == FlowState.Instructions, 3000);
            Assert.That(flow.PendingOrders, Is.Empty);
            Assert.That(flow.ActiveOrder.PatientRef, Is.EqualTo("P-FILE"));
        }

        [Test]
        public void Live_PostsOnStateChange_AndAtInterval_FinishedOnce_ThenUploads()
        {
            var (sync, files, portal) = NewSync(withCode: false, withToken: true);
            var storage = new RecordingSink(files);
            var flow = new PatientFlow(Env.Config, Env.Headset, Env.Lum, storage, new ExamOrder { PresetId = "24-2-zest-od", AgeYears = 50, PatientRef = "P-LIVE", Seed = 12 });
            var d = new FlowDriver(flow, 12);
            Run(portal, sync, d, () => flow.IsFinished && files.Files.Keys.Count(k => k.EndsWith(".sent")) == 2);

            var live = portal.LiveBodies;
            Assert.That(live.Count, Is.GreaterThan(10));
            Assert.That(live.Select(b => (long)b["seq"]), Is.Ordered.Ascending, "seq strictly increasing");
            Assert.That(live.Select(b => (long)b["seq"]).Distinct().Count(), Is.EqualTo(live.Count));
            var states = live.Select(b => (string)b["state"]).ToList();
            foreach (var s in new[] { "Welcome", "Instructions", "EyeIntro", "Exam", "Done" })
                Assert.That(states, Does.Contain(s), "every state change is posted");
            Assert.That(live.Count(b => (bool)b["finished"]), Is.EqualTo(1), "finished=true exactly once");
            Assert.That((string)live.Last()["outcome"], Is.EqualTo("completed"));
            Assert.That((string)live.Last()["patientRef"], Is.EqualTo("P-LIVE"));
            Assert.That((string)live.First()["sessionId"], Is.EqualTo(flow.Log.SessionId));

            // cadence during the exam: consecutive posts never more than liveIntervalMs + one frame apart
            var examPosts = portal.Sent.Where(c => c.Purpose == "live").ToList();
            Assert.That(examPosts.Count, Is.EqualTo(live.Count));
            double maxGap = 0;
            var times = new List<double>();
            // replay the driver clock: a post per frame-stamped request order; use the elapsedMs the headset reported instead
            var elapsed = live.Select(b => (double)b["elapsedMs"]).ToList();
            for (int i = 1; i < elapsed.Count; i++) maxGap = Math.Max(maxGap, elapsed[i] - elapsed[i - 1]);
            Assert.That(maxGap, Is.LessThanOrEqualTo(Env.Headset.Sync.LiveIntervalMs + 2 * 1000.0 / FlowDriver.Hz + 1));

            var exam = live.Where(b => b["exam"].Type == JTokenType.Object).Select(b => (JObject)b["exam"]).Last();
            Assert.That((string)exam["eye"], Is.EqualTo("OD"));
            Assert.That((string)exam["gridId"], Is.EqualTo("24-2"));
            var locs = (JArray)exam["locations"];
            Assert.That(locs.Count, Is.EqualTo(54));
            var finishedLocs = locs.Where(l => (string)l["state"] == "Finished" && l["db"].Type != JTokenType.Null).ToList();
            Assert.That(finishedLocs.Count, Is.GreaterThan(40));
            var l0 = finishedLocs[0];
            Assert.That((double)l0["hfaEqDb"], Is.EqualTo((double)l0["db"] + (double)exam["hfaOffsetDb"]).Within(0.011));
            Assert.That((double)exam["minMeasurableHfaDb"], Is.EqualTo(Env.Config.Strategies.MinStimulusDb + (double)exam["hfaOffsetDb"]).Within(0.011));
            var rel = (JObject)exam["reliability"];
            Assert.That((double)rel["fixationLosses"]["alertLimit"], Is.EqualTo(Env.Config.Reliability.FixationLoss.AlertLimit).Within(1e-9));
            Assert.That((int)rel["totalPresentations"], Is.GreaterThan(100));
            Assert.That(exam["last"]["kind"], Is.Not.Null);

            // upload after the finished snapshot: both files, with their hmac, then .sent receipts
            var upload = portal.Sent.Single(c => c.Purpose == "upload");
            Assert.That(portal.Sent.IndexOf(upload), Is.GreaterThan(portal.Sent.IndexOf(examPosts.Last())), "upload only after the live feed closed");
            var body = JObject.Parse(upload.JsonBody);
            var names = ((JArray)body["files"]).Select(f => (string)f["name"]).ToList();
            Assert.That(names.Count(n => n.StartsWith("exam_")), Is.EqualTo(1));
            Assert.That(names.Count(n => n.StartsWith("session_")), Is.EqualTo(1));
            Assert.That(((JArray)body["files"]).All(f => ((string)f["hmac"]).Length == 64));
            Assert.That((string)body["sessionId"], Is.EqualTo(flow.Log.SessionId));
            foreach (var n in names)
            {
                var receipt = JObject.Parse(files.ReadText(Paths.Exam(n) + ".sent"));
                Assert.That((string)receipt["status"], Is.EqualTo("imported"));
                Assert.That((string)receipt["examDbId"], Is.EqualTo("db-" + n));
            }
            // nothing left to send
            for (int i = 0; i < 600; i++) { d.Frame(); portal.Frame(sync, d.Now, flow); }
            Assert.That(portal.Count("upload"), Is.EqualTo(1));
            Assert.That(portal.Count("live"), Is.EqualTo(live.Count), "no live posts after finished");
        }

        [Test]
        public void PauseCommand_PausesExam_AckedOnce_ResumeAndAbortWork()
        {
            var (sync, _, portal) = NewSync(withCode: false, withToken: true);
            var sink = new MemorySink();
            var flow = new PatientFlow(Env.Config, Env.Headset, Env.Lum, sink, new ExamOrder { PresetId = "24-2-zest", AgeYears = 45, Seed = 5 });
            var d = new FlowDriver(flow, 5);
            Run(portal, sync, d, () => flow.State == FlowState.Exam && flow.CurrentSession.BuildRecord().Presentations.Count > 20);
            portal.Commands.Enqueue(new JObject { ["id"] = "cmd-pause", ["type"] = "pause", ["issuedUtc"] = "2026-10-18T12:00:00Z", ["by"] = "Dra. Teste" });
            Run(portal, sync, d, () => flow.State == FlowState.Paused, 3000);
            Assert.That(flow.View.MessageKey, Is.EqualTo("pause"));
            Assert.That(flow.CurrentSession.BuildRecord().Presentations.Last().Disposition, Is.EqualTo("aborted"), "presentation in progress discarded like a patient hold");
            // the server keeps returning the command until acked: it must not be applied twice
            portal.Commands.Enqueue(new JObject { ["id"] = "cmd-pause", ["type"] = "pause" });
            for (int i = 0; i < 200; i++) { d.Frame(); portal.Frame(sync, d.Now, flow); }
            Assert.That(flow.State, Is.EqualTo(FlowState.Paused).Or.EqualTo(FlowState.Resuming).Or.EqualTo(FlowState.Exam));
            var acks = portal.LiveBodies.SelectMany(b => ((JArray)b["ackCommandIds"]).Select(a => (string)a)).ToList();
            Assert.That(acks.Count(a => a == "cmd-pause"), Is.EqualTo(1), "acked exactly once");
            Assert.That(flow.Log.Events.Count(e => e.Type == "pause" && e.Detail == "operator"), Is.EqualTo(1));

            // the simulated patient taps on the pause screen on its own; test resume while the headset is off then on
            Run(portal, sync, d, () => flow.State == FlowState.Exam);
            d.AutoResume = false; // the simulated patient no longer taps on the pause screen
            flow.SetUserPresent(false, d.Now);
            Assert.That(flow.State, Is.EqualTo(FlowState.Paused));
            portal.Commands.Enqueue(new JObject { ["id"] = "cmd-resume-1", ["type"] = "resume" });
            for (int i = 0; i < 100; i++) { d.Frame(); portal.Frame(sync, d.Now, flow); }
            Assert.That(flow.State, Is.EqualTo(FlowState.Paused), "resume ignored while the headset is off");
            flow.SetUserPresent(true, d.Now);
            portal.Commands.Enqueue(new JObject { ["id"] = "cmd-resume-2", ["type"] = "resume" });
            Run(portal, sync, d, () => flow.State == FlowState.Resuming || flow.State == FlowState.Exam, 3000);
            Assert.That(flow.Log.Events.Any(e => e.Type == "operatorResume"));

            Run(portal, sync, d, () => flow.State == FlowState.Exam);
            portal.Commands.Enqueue(new JObject { ["id"] = "cmd-abort", ["type"] = "abort", ["by"] = "Dra. Teste" });
            Run(portal, sync, d, () => flow.IsFinished, 3000);
            Assert.That(flow.State, Is.EqualTo(FlowState.Aborted));
            Assert.That(sink.Exams.Single().Aborted);
            Assert.That(sink.Exams.Single().Reliability.Pauses.Any(p => p.Reason == "operator"));
            Run(portal, sync, d, () => portal.LiveBodies.Any(b => (bool)b["finished"]), 3000);
            Assert.That((string)portal.LiveBodies.Last()["outcome"], Is.EqualTo("aborted"));
        }

        [Test]
        public void Upload_KeepsErrorsQueued_AndRetriesAfterBackoff()
        {
            var (sync, files, portal) = NewSync(withCode: false, withToken: true);
            files.Files[Paths.Exam("exam_20261018T100000_OD_aaa.json")] = "{\"ExamId\":\"aaa\"}";
            files.Files[Paths.Exam("exam_20261018T100000_OD_aaa.json.hmac")] = new string('a', 64);
            files.Files[Paths.Exam("exam_20261018T100500_OS_bbb.json")] = "{\"ExamId\":\"bbb\"}";
            files.Files[Paths.Exam("session_20261018T100000_s1.json")] = "{\"SessionId\":\"s1\"}";
            files.Files[Paths.Exam("session_20261018T090000_old.json")] = "{}";
            files.Files[Paths.Exam("session_20261018T090000_old.json.sent")] = "{}";
            portal.Handlers["upload"] = c =>
            {
                var results = new JArray();
                foreach (var f in (JArray)JObject.Parse(c.JsonBody)["files"])
                {
                    string n = (string)f["name"];
                    results.Add(new JObject { ["name"] = n, ["status"] = n.Contains("bbb") ? "error" : n.Contains("aaa") ? "duplicate" : "imported", ["message"] = "x", ["examDbId"] = JValue.CreateNull() });
                }
                return (200, new JObject { ["schema"] = PortalSync.Schema, ["results"] = results }.ToString());
            };
            double now = 1000;
            portal.Offline = true;
            for (int i = 0; i < 90; i++) { now += 1000.0 / FlowDriver.Hz; portal.Frame(sync, now, null); } // ~1 s offline
            Assert.That(portal.Count("orders"), Is.EqualTo(1), "one attempt, then backoff");
            Assert.That(sync.ConsecutiveFailures, Is.EqualTo(2), "orders and upload both failed: backoff now 5 s");
            Assert.That(sync.Status, Does.Contain("failure"));
            portal.Offline = false;
            for (int i = 0; i < 90; i++) { now += 1000.0 / FlowDriver.Hz; portal.Frame(sync, now, null); }
            Assert.That(portal.Count("orders"), Is.EqualTo(1), "still inside the 5 s backoff");
            for (int i = 0; i < 90 * 8; i++) { now += 1000.0 / FlowDriver.Hz; portal.Frame(sync, now, null); }
            Assert.That(portal.Count("orders"), Is.EqualTo(2));
            Assert.That(portal.Count("upload"), Is.GreaterThanOrEqualTo(1));
            var first = JObject.Parse(portal.Sent.First(c => c.Purpose == "upload").JsonBody);
            var names = ((JArray)first["files"]).Select(f => (string)f["name"]).ToList();
            Assert.That(names, Is.EquivalentTo(new[] { "exam_20261018T100000_OD_aaa.json", "exam_20261018T100500_OS_bbb.json", "session_20261018T100000_s1.json" }), "already-sent file skipped");
            Assert.That((string)((JArray)first["files"])[0]["hmac"], Is.EqualTo(new string('a', 64)));
            Assert.That(((JArray)first["files"]).First(f => (string)f["name"] == "exam_20261018T100500_OS_bbb.json")["hmac"].Type, Is.EqualTo(JTokenType.Null));
            Assert.That(files.Exists(Paths.Exam("exam_20261018T100000_OD_aaa.json.sent")), "duplicate closes the file");
            Assert.That(files.Exists(Paths.Exam("session_20261018T100000_s1.json.sent")));
            Assert.That(files.Exists(Paths.Exam("exam_20261018T100500_OS_bbb.json.sent")), Is.False, "error stays queued");
            Assert.That(JObject.Parse(files.ReadText(Paths.Exam("exam_20261018T100000_OD_aaa.json.sent")))["status"].ToString(), Is.EqualTo("duplicate"));
            // the queued file is retried on the next scan, alone
            for (int i = 0; i < 90 * 6; i++) { now += 1000.0 / FlowDriver.Hz; portal.Frame(sync, now, null); }
            var last = JObject.Parse(portal.Sent.Last(c => c.Purpose == "upload").JsonBody);
            Assert.That(((JArray)last["files"]).Select(f => (string)f["name"]), Is.EqualTo(new[] { "exam_20261018T100500_OS_bbb.json" }));
        }

        [Test]
        public void RevokedToken_StopsSync_UntilPairedAgain()
        {
            var (sync, files, portal) = NewSync(withCode: true, withToken: true);
            // token file present but the portal says 401 on the first call made with the old token: pairing code wins first
            var flow = ManualFlow();
            var d = new FlowDriver(flow, 1);
            Run(portal, sync, d, () => portal.Count("orders") >= 1, 5000);
            Assert.That(portal.Sent.First().Purpose, Is.EqualTo("pair"), "a pairing code re-pairs even with a token");
            Assert.That(portal.Sent.First(c => c.Purpose == "orders").BearerToken, Is.EqualTo("tok-123"));

            portal.Handlers["live"] = c => (401, "{\"schema\":\"perimetry-sync/1\",\"error\":\"unauthorized\"}");
            for (int i = 0; i < 300; i++) { d.Frame(); portal.Frame(sync, d.Now, flow); }
            int sent = portal.Sent.Count;
            for (int i = 0; i < 300; i++) { d.Frame(); portal.Frame(sync, d.Now, flow); }
            Assert.That(portal.Sent.Count, Is.EqualTo(sent), "nothing more after a 401");
            Assert.That(sync.IsPaired, Is.False);
            Assert.That(sync.Status, Does.Contain("token rejected"));
        }

        [Test]
        public void Disabled_InConfig_NeverTouchesTheNetwork()
        {
            var h = Env.Headset;
            h.Sync.Enabled = false;
            var files = new FakeFiles { Files = { [Paths.Portal] = "{\"url\":\"http://x\",\"pairingCode\":\"AAAA-AAAA\"}" } };
            var sync = new PortalSync(Env.Config, h, Paths, files, Identity);
            Assert.That(sync.Tick(1000, ManualFlow()), Is.Empty);
            Assert.That(sync.Status, Is.EqualTo("sync: disabled"));
        }

        /// <summary>Writes the exam files like <see cref="ExamStorage"/> would, into the fake file system, with HMAC sidecars.</summary>
        private sealed class RecordingSink : IExamSink
        {
            private readonly FakeFiles _files;
            public RecordingSink(FakeFiles files) { _files = files; }
            public void SaveExam(Perimetry.Core.Exam.ExamRecord exam) => Write($"exam_{exam.StartedUtc:yyyyMMdd'T'HHmmss}_{exam.Settings.Eye}_{exam.ExamId}.json", exam);
            public void SaveSessionLog(SessionLog log) => Write($"session_{log.StartedUtc:yyyyMMdd'T'HHmmss}_{log.SessionId}.json", log);
            private void Write(string name, object payload)
            {
                string text = Newtonsoft.Json.JsonConvert.SerializeObject(payload, ExamStorage.Json);
                _files.Files[Paths.Exam(name)] = text;
                using (var h = new System.Security.Cryptography.HMACSHA256(new byte[32]))
                    _files.Files[Paths.Exam(name) + ".hmac"] = BitConverter.ToString(h.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
