using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;
using Perimetry.Core.Config;
using Perimetry.Core.Exam;
using Perimetry.Core.Strategies;

namespace Perimetry.Headset.Logic
{
    /// <summary>
    /// Builds the live-panel snapshot (docs/SYNC_API.md, POST /api/headset/live) from the flow. Pure
    /// presentation of what <see cref="ExamSession.BuildRecord"/> already holds: no clinical value is computed
    /// here beyond the dB offset the /core scale defines (hfaEqDb = device dB + HfaOffsetDb).
    /// </summary>
    public static class LiveSnapshot
    {
        public static JObject Build(PatientFlow flow, ClinicalConfig cfg, long seq, DateTime sentUtc, IEnumerable<string> ackCommandIds)
        {
            var order = flow.ActiveOrder;
            var o = new JObject
            {
                ["schema"] = PortalSync.Schema,
                ["sessionId"] = flow.Log.SessionId,
                ["seq"] = seq,
                ["sentUtc"] = Iso(sentUtc),
                ["state"] = flow.State.ToString(),
                ["paused"] = flow.IsPaused,
                ["finished"] = flow.IsFinished,
                ["outcome"] = flow.IsFinished ? (JToken)(flow.Log.Outcome ?? flow.State.ToString().ToLowerInvariant()) : JValue.CreateNull(),
                ["patientRef"] = (JToken)order?.PatientRef ?? JValue.CreateNull(),
                ["orderId"] = (JToken)order?.Id ?? JValue.CreateNull(),
                ["presetId"] = flow.Preset.Id,
                ["ageYears"] = flow.AgeYears,
                ["eye"] = flow.CurrentEye.HasValue ? (JToken)flow.CurrentEye.Value.ToString() : JValue.CreateNull(),
                ["gazeAvailable"] = flow.GazeAvailable,
                ["fixation"] = new JObject
                {
                    ["kind"] = flow.View.FixationTarget.ToString(),
                    ["x"] = Round(flow.View.FixationX),
                    ["y"] = Round(flow.View.FixationY),
                },
                ["startedUtc"] = Iso(flow.SessionStartedUtc),
                ["elapsedMs"] = Math.Round(flow.ElapsedMs),
                ["exam"] = Exam(flow, cfg),
                ["ackCommandIds"] = new JArray(ackCommandIds ?? new string[0]),
            };
            return o;
        }

        private static JToken Exam(PatientFlow flow, ClinicalConfig cfg)
        {
            var session = flow.CurrentSession;
            if (session == null) return JValue.CreateNull();
            var rec = session.BuildRecord();
            var locs = new JArray();
            foreach (var l in rec.Locations)
            {
                var state = session.Strategy.StateOf(l.Index);
                bool finished = state == ThresholdStrategy.LocationState.Finished && !double.IsNaN(l.ThresholdDb);
                locs.Add(new JObject
                {
                    ["i"] = l.Index,
                    ["x"] = l.X,
                    ["y"] = l.Y,
                    ["blindSpot"] = l.IsBlindSpot,
                    ["state"] = state.ToString(),
                    ["db"] = finished ? (JToken)Round(l.ThresholdDb) : JValue.CreateNull(),
                    ["hfaEqDb"] = finished ? (JToken)Round(l.ThresholdDb + rec.HfaOffsetDb) : JValue.CreateNull(),
                    ["censoredLow"] = l.CensoredLow,
                    ["presentations"] = l.Presentations,
                });
            }
            var r = rec.Reliability;
            JToken last = JValue.CreateNull();
            if (rec.Presentations.Count > 0)
            {
                var p = rec.Presentations[rec.Presentations.Count - 1];
                last = new JObject
                {
                    ["x"] = p.Request.FieldX,
                    ["y"] = p.Request.FieldY,
                    ["targetX"] = Round(p.Request.TargetX),
                    ["targetY"] = Round(p.Request.TargetY),
                    ["db"] = p.Request.StimulusShown && !double.IsNaN(p.Request.Db) ? (JToken)Round(p.Request.Db) : JValue.CreateNull(),
                    ["seen"] = p.Result != null && p.Result.Responded,
                    ["kind"] = p.Request.Kind.ToString(),
                    ["tMs"] = p.Result != null ? Math.Round(p.Result.OnsetMs) : 0,
                };
            }
            bool fovealDone = rec.Foveal != null && !double.IsNaN(rec.Foveal.ThresholdDb);
            return new JObject
            {
                ["examId"] = rec.ExamId,
                ["gridId"] = rec.Settings.GridId,
                ["strategy"] = rec.Settings.Strategy.ToString(),
                ["eye"] = rec.Settings.Eye.ToString(),
                ["fixationMode"] = rec.Settings.FixationMode.ToString(),
                ["responseMode"] = rec.Settings.ResponseMode.ToString(),
                ["fovealThreshold"] = rec.Settings.FovealThreshold,
                ["foveal"] = fovealDone
                    ? new JObject
                    {
                        ["db"] = Round(rec.Foveal.ThresholdDb),
                        ["hfaEqDb"] = Round(rec.Foveal.ThresholdDb + rec.HfaOffsetDb),
                        ["censoredLow"] = rec.Foveal.CensoredLow,
                        ["presentations"] = rec.Foveal.Presentations,
                    }
                    : (JToken)JValue.CreateNull(),
                ["hfaOffsetDb"] = Round(rec.HfaOffsetDb),
                ["minMeasurableHfaDb"] = Round(cfg.Strategies.MinStimulusDb + rec.HfaOffsetDb),
                ["locations"] = locs,
                ["reliability"] = new JObject
                {
                    ["fixationLosses"] = Catch(r.FixationLosses),
                    ["falsePositives"] = Catch(r.FalsePositives),
                    ["falseNegatives"] = Catch(r.FalseNegatives),
                    ["totalPresentations"] = r.TotalPresentations,
                    ["blindSpotFound"] = r.BlindSpotFound,
                    ["blindSpotRemaps"] = r.BlindSpotRemaps,
                    ["durationMs"] = Math.Round(Math.Max(0, r.DurationMs)),
                    ["gazeTracked"] = r.GazeTracked,
                    ["gazeRejectedPresentations"] = r.GazeRejectedPresentations,
                    ["gazeMeanErrorDeg"] = r.GazeTracked && !double.IsNaN(r.GazeMeanErrorDeg) ? (JToken)Round(r.GazeMeanErrorDeg) : JValue.CreateNull(),
                },
                ["last"] = last,
            };
        }

        private static JObject Catch(CatchCount c) => new JObject { ["errors"] = c.Errors, ["trials"] = c.Trials, ["alertLimit"] = c.AlertLimit };
        private static double Round(double v) => Math.Round(v, 2);
        public static string Iso(DateTime utc) => utc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }
}
