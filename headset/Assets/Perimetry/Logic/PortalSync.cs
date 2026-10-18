using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Perimetry.Core.Config;

namespace Perimetry.Headset.Logic
{
    /// <summary>One HTTP request the presentation layer must issue; answered through <see cref="PortalSync.Complete"/> or <see cref="PortalSync.Fail"/>.</summary>
    public sealed class HttpCall
    {
        public int Id;
        public string Method;
        public string Url;
        /// <summary>Null for the pairing call.</summary>
        public string BearerToken;
        /// <summary>UTF-8 JSON body, null for GET.</summary>
        public string JsonBody;
        /// <summary>"pair", "orders", "upload" or "live".</summary>
        public string Purpose;
    }

    /// <summary>
    /// Sans-IO client of the portal sync API (docs/SYNC_API.md). <see cref="Tick"/> decides which requests to
    /// issue from the files on disk and the flow's state; the Unity layer sends them and feeds the answers back.
    /// Nothing here is needed for the exam: with no portal.json / portal-token.json the client stays idle,
    /// and every network failure only delays the next attempt (retryBackoffMs).
    /// </summary>
    public sealed class PortalSync
    {
        public const string Schema = "perimetry-sync/1";
        public const string PurposePair = "pair", PurposeOrders = "orders", PurposeUpload = "upload", PurposeLive = "live";

        private static readonly IReadOnlyList<HttpCall> NoCalls = new List<HttpCall>();

        private readonly ClinicalConfig _cfg;
        private readonly SyncConfig _sync;
        private readonly SyncPaths _paths;
        private readonly ISyncFiles _files;
        private readonly SyncIdentity _id;
        private readonly Func<DateTime> _utcNow;
        private readonly Dictionary<int, HttpCall> _inflight = new Dictionary<int, HttpCall>();
        private readonly List<string> _events = new List<string>();
        private readonly HashSet<string> _appliedCommands = new HashSet<string>();
        private readonly List<string> _ackPending = new List<string>();
        private readonly List<string> _ackInFlight = new List<string>();
        private readonly List<ExamOrder> _orders = new List<ExamOrder>();

        private bool _loaded, _pairRejected, _tokenRejected, _ordersFetched, _ordersApplied, _liveClosed;
        private PortalFile _portal;
        private TokenFile _token;
        private PatientFlow _flow;
        private double _nowMs, _blockedUntilMs, _lastLiveMs, _lastScanMs = -1e12;
        private bool _liveFinishedPending;
        private int _failures, _nextId, _lastStateChanges = -1;
        private long _liveSeq;
        private List<string> _uploading;

        public PortalSync(ClinicalConfig cfg, HeadsetConfig headset, SyncPaths paths, ISyncFiles files, SyncIdentity identity, Func<DateTime> utcNow = null)
        {
            _cfg = cfg; _sync = headset.Sync; _paths = paths; _files = files; _id = identity ?? new SyncIdentity();
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        /// <summary>Pending orders received from the portal (also handed to the flow through <see cref="PatientFlow.SetPendingOrders"/>).</summary>
        public IReadOnlyList<ExamOrder> Orders => _orders;
        /// <summary>Last notable events (newest last), for the operator display.</summary>
        public IReadOnlyList<string> Events => _events;
        public bool IsPaired => _token != null && !_tokenRejected;
        public string PortalUrl => _token?.Url ?? _portal?.Url;
        public long LiveSeq => _liveSeq;
        public int ConsecutiveFailures => _failures;
        /// <summary>One line for the operator display.</summary>
        public string Status { get; private set; } = "sync: off";

        // ---------------------------------------------------------------- tick

        public IReadOnlyList<HttpCall> Tick(double nowMs, PatientFlow flow)
        {
            _nowMs = Math.Max(_nowMs, nowMs);
            if (flow != null) _flow = flow;
            List<HttpCall> calls = null;
            try
            {
                if (!_sync.Enabled) { Status = "sync: disabled"; return NoCalls; }
                if (!_loaded) Load();
                if (_token == null && _portal == null) { Status = "sync: no portal.json"; return NoCalls; }
                if (_nowMs < _blockedUntilMs) return NoCalls;

                if (_portal?.PairingCode != null && !_pairRejected)
                {
                    if (!InFlight(PurposePair)) Add(ref calls, Pair());
                    return calls ?? NoCalls; // nothing else until paired
                }
                if (_token == null || _tokenRejected) return NoCalls;

                if (!_ordersFetched && !InFlight(PurposeOrders)) Add(ref calls, Get(PurposeOrders, "/api/headset/orders"));
                if (_ordersFetched && !_ordersApplied && _flow != null) { _flow.SetPendingOrders(_orders); _ordersApplied = true; }

                if (_flow != null && !_liveClosed && !InFlight(PurposeLive))
                {
                    bool changed = _flow.StateChanges != _lastStateChanges;
                    bool due = _nowMs - _lastLiveMs >= _sync.LiveIntervalMs;
                    if (changed || due || _flow.IsFinished) Add(ref calls, Live());
                }

                bool idle = _flow == null || _flow.IsFinished
                    || _flow.State == FlowState.Welcome || _flow.State == FlowState.SetupOrder || _flow.State == FlowState.SetupPreset || _flow.State == FlowState.SetupAge;
                bool finishedSent = _flow == null || !_flow.IsFinished || _liveClosed;
                if (idle && finishedSent && !InFlight(PurposeUpload) && _nowMs - _lastScanMs >= 5000)
                {
                    _lastScanMs = _nowMs;
                    var pending = Unsent();
                    if (pending.Count > 0) Add(ref calls, Upload(pending));
                }
                Status = StatusLine();
            }
            catch (Exception e) { Note("tick error: " + e.GetType().Name + ": " + e.Message); }
            return calls ?? NoCalls;
        }

        private static void Add(ref List<HttpCall> calls, HttpCall c) { (calls ??= new List<HttpCall>()).Add(c); }

        private string StatusLine()
        {
            string s = "sync: " + (_token != null ? "paired " + _token.Url : "pairing");
            if (_flow != null && !_liveClosed) s += $" · live #{_liveSeq}";
            if (_failures > 0) s += $" · {_failures} failure(s), retry in {Math.Max(0, (_blockedUntilMs - _nowMs) / 1000):F0} s";
            return s;
        }

        // ---------------------------------------------------------------- answers

        public void Complete(int id, int httpStatus, string body)
        {
            if (!_inflight.TryGetValue(id, out var call)) return;
            _inflight.Remove(id);
            try
            {
                if (httpStatus == 401 && call.Purpose != PurposePair)
                {
                    _tokenRejected = true;
                    Note("token rejected by the portal (401): pair again");
                    Status = "sync: token rejected, pair again";
                    return;
                }
                if (httpStatus < 200 || httpStatus >= 300)
                {
                    if (call.Purpose == PurposePair && (httpStatus == 403 || httpStatus == 400))
                    {
                        _pairRejected = true;
                        Note("pairing rejected (" + httpStatus + "): " + Error(body));
                        Status = "sync: pairing code rejected";
                        return;
                    }
                    if (call.Purpose == PurposeUpload) _uploading = null;
                    if (call.Purpose == PurposeLive) ReturnAcks();
                    Backoff("HTTP " + httpStatus + " on " + call.Purpose);
                    return;
                }
                var json = body != null && body.TrimStart().StartsWith("{") ? JObject.Parse(body) : new JObject();
                switch (call.Purpose)
                {
                    case PurposePair: OnPaired(json); break;
                    case PurposeOrders: OnOrders(json); break;
                    case PurposeUpload: OnUploaded(json); break;
                    case PurposeLive: OnLive(json); break;
                }
                _failures = 0;
                _blockedUntilMs = 0;
                Status = StatusLine();
            }
            catch (Exception e)
            {
                if (call.Purpose == PurposeUpload) _uploading = null;
                if (call.Purpose == PurposeLive) ReturnAcks();
                Backoff("bad response on " + call.Purpose + ": " + e.GetType().Name + ": " + e.Message);
            }
        }

        public void Fail(int id, string error)
        {
            if (!_inflight.TryGetValue(id, out var call)) return;
            _inflight.Remove(id);
            if (call.Purpose == PurposeUpload) _uploading = null;
            if (call.Purpose == PurposeLive) ReturnAcks();
            Backoff(call.Purpose + ": " + (error ?? "network error"));
        }

        private void Backoff(string why)
        {
            _failures++;
            var steps = _sync.RetryBackoffMs;
            int wait = steps[Math.Min(_failures, steps.Count) - 1];
            _blockedUntilMs = _nowMs + wait;
            Note(why + $" (retry in {wait} ms)");
            Status = StatusLine();
        }

        // ---------------------------------------------------------------- requests

        private HttpCall Pair()
        {
            var body = new JObject
            {
                ["schema"] = Schema,
                ["code"] = _portal.PairingCode,
                ["serial"] = _id.Serial,
                ["model"] = _id.Model,
                ["hmacKey"] = _id.HmacKeyBase64,
                ["software"] = _id.Software,
            };
            return Issue(PurposePair, "POST", _portal.Url, "/api/headset/pair", null, body);
        }

        private HttpCall Get(string purpose, string path) => Issue(purpose, "GET", _token.Url, path, _token.Token, null);

        private HttpCall Live()
        {
            _lastLiveMs = _nowMs;
            _lastStateChanges = _flow.StateChanges;
            _ackInFlight.Clear();
            _ackInFlight.AddRange(_ackPending);
            _ackPending.Clear();
            var body = LiveSnapshot.Build(_flow, _cfg, ++_liveSeq, _utcNow(), _ackInFlight);
            _liveFinishedPending = _flow.IsFinished; // the finished=true snapshot closes the live feed once it is acknowledged
            return Issue(PurposeLive, "POST", _token.Url, "/api/headset/live", _token.Token, body);
        }

        private HttpCall Upload(List<string> names)
        {
            _uploading = names;
            var files = new JArray();
            foreach (var n in names)
            {
                string hmacPath = _paths.Exam(n) + ".hmac";
                files.Add(new JObject
                {
                    ["name"] = n,
                    ["text"] = _files.ReadText(_paths.Exam(n)),
                    ["hmac"] = _files.Exists(hmacPath) ? (JToken)_files.ReadText(hmacPath).Trim() : JValue.CreateNull(),
                });
            }
            var body = new JObject { ["schema"] = Schema, ["sessionId"] = (JToken)_flow?.Log.SessionId ?? JValue.CreateNull(), ["files"] = files };
            return Issue(PurposeUpload, "POST", _token.Url, "/api/headset/exams", _token.Token, body);
        }

        private HttpCall Issue(string purpose, string method, string baseUrl, string path, string token, JObject body)
        {
            var c = new HttpCall
            {
                Id = ++_nextId, Method = method, Url = baseUrl.TrimEnd('/') + path, BearerToken = token, Purpose = purpose,
                JsonBody = body?.ToString(Formatting.None),
            };
            _inflight[c.Id] = c;
            return c;
        }

        private bool InFlight(string purpose) => _inflight.Values.Any(c => c.Purpose == purpose);

        private void ReturnAcks()
        {
            _liveFinishedPending = false;
            // the post carrying these acks failed: ack again next time
            _ackPending.InsertRange(0, _ackInFlight);
            _ackInFlight.Clear();
        }

        // ---------------------------------------------------------------- handlers

        private void OnPaired(JObject json)
        {
            string token = (string)json["token"], deviceId = (string)json["deviceId"];
            if (string.IsNullOrEmpty(token)) throw new FormatException("pair response without token");
            _token = new TokenFile { Url = _portal.Url, DeviceId = deviceId, Token = token, PairedUtc = LiveSnapshot.Iso(_utcNow()) };
            _files.WriteText(_paths.Token, JsonConvert.SerializeObject(_token, Formatting.Indented));
            _files.Move(_paths.Portal, _paths.UsedPortal(_utcNow()));
            _portal = null;
            _tokenRejected = false;
            _ordersFetched = false; _ordersApplied = false;
            Note("paired with " + _token.Url + " as device " + deviceId + " (" + (string)json["portalName"] + ")");
        }

        private void OnOrders(JObject json)
        {
            _orders.Clear();
            foreach (var o in json["orders"] as JArray ?? new JArray())
            {
                _orders.Add(new ExamOrder
                {
                    Id = (string)o["id"],
                    PatientRef = (string)o["patientRef"],
                    AgeYears = o["ageYears"]?.Type == JTokenType.Null ? null : (double?)o["ageYears"],
                    RefractiveCorrection = (string)o["refractiveCorrection"],
                    PresetId = (string)o["presetId"],
                    Seed = o["seed"] == null || o["seed"].Type == JTokenType.Null ? null : (ulong?)(long)o["seed"],
                });
            }
            _ordersFetched = true;
            Note(_orders.Count + " pending order(s)");
        }

        private void OnUploaded(JObject json)
        {
            int closed = 0, errors = 0;
            foreach (var r in json["results"] as JArray ?? new JArray())
            {
                string name = (string)r["name"], status = (string)r["status"];
                if (name == null || _uploading == null || !_uploading.Contains(name)) continue;
                if (status == "imported" || status == "duplicate")
                {
                    var receipt = new JObject { ["status"] = status, ["examDbId"] = r["examDbId"] ?? JValue.CreateNull(), ["message"] = r["message"] ?? "", ["sentUtc"] = LiveSnapshot.Iso(_utcNow()) };
                    _files.WriteText(_paths.Exam(name) + SyncPaths.SentSuffix, receipt.ToString(Formatting.Indented));
                    closed++;
                }
                else { errors++; Note("portal rejected " + name + ": " + (string)r["message"]); }
            }
            _uploading = null;
            _lastScanMs = 0; // look for the rest right away
            Note($"uploaded {closed} file(s)" + (errors > 0 ? $", {errors} kept in the queue" : ""));
        }

        private void OnLive(JObject json)
        {
            _ackInFlight.Clear();
            if (_liveFinishedPending) { _liveClosed = true; _liveFinishedPending = false; }
            foreach (var c in json["commands"] as JArray ?? new JArray())
            {
                string id = (string)c["id"], type = (string)c["type"];
                if (id == null || !_appliedCommands.Add(id)) continue;
                _ackPending.Add(id);
                if (_flow == null) continue;
                switch (type)
                {
                    case "pause": _flow.OperatorPause(_nowMs); break;
                    case "resume": _flow.OperatorResume(_nowMs); break;
                    case "abort": _flow.OperatorAbort(_nowMs); break;
                    default: Note("unknown command " + type); continue;
                }
                Note("command " + type + " from " + ((string)c["by"] ?? "portal"));
                _flow.Note("sync", "command " + type + " (" + id + ")");
            }
        }

        // ---------------------------------------------------------------- files

        private void Load()
        {
            _loaded = true;
            if (_files.Exists(_paths.Token))
            {
                try { _token = JsonConvert.DeserializeObject<TokenFile>(_files.ReadText(_paths.Token)); }
                catch (Exception e) { Note("portal-token.json unreadable: " + e.Message); }
                if (_token != null && (string.IsNullOrEmpty(_token.Url) || string.IsNullOrEmpty(_token.Token))) _token = null;
            }
            if (_files.Exists(_paths.Portal))
            {
                try { _portal = JsonConvert.DeserializeObject<PortalFile>(_files.ReadText(_paths.Portal)); }
                catch (Exception e) { Note("portal.json unreadable: " + e.Message); }
                if (_portal != null && string.IsNullOrEmpty(_portal.Url)) _portal = null;
                if (_portal != null && string.IsNullOrEmpty(_portal.PairingCode))
                {
                    // url only: keep it as the portal address when there is no token yet, otherwise ignore
                    if (_token == null) Note("portal.json has no pairingCode: cannot pair");
                    _portal = null;
                }
            }
            Status = StatusLine();
        }

        /// <summary>Exam and session files without a ".sent" receipt, oldest first, at most maxFilesPerUpload.</summary>
        private List<string> Unsent()
        {
            var names = _files.List(_paths.ExamsDir);
            var set = new HashSet<string>(names);
            return names
                .Where(n => n.EndsWith(".json") && (n.StartsWith("exam_") || n.StartsWith("session_")) && !set.Contains(n + SyncPaths.SentSuffix))
                .Take(_sync.MaxFilesPerUpload)
                .ToList();
        }

        private void Note(string s)
        {
            _events.Add(s);
            if (_events.Count > 50) _events.RemoveAt(0);
            _flow?.Note("sync", s);
        }

        private static string Error(string body)
        {
            try { var j = JObject.Parse(body); return (string)j["message"] ?? (string)j["error"] ?? body; }
            catch { return body ?? ""; }
        }

        private sealed class PortalFile
        {
            [JsonProperty("url")] public string Url { get; set; }
            [JsonProperty("pairingCode")] public string PairingCode { get; set; }
        }

        private sealed class TokenFile
        {
            [JsonProperty("url")] public string Url { get; set; }
            [JsonProperty("deviceId")] public string DeviceId { get; set; }
            [JsonProperty("token")] public string Token { get; set; }
            [JsonProperty("pairedUtc")] public string PairedUtc { get; set; }
        }
    }
}
