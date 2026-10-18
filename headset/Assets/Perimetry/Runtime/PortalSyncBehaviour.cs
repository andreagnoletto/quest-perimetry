using System.Collections;
using System.Text;
using Perimetry.Headset.Logic;
using UnityEngine;
using UnityEngine.Networking;

namespace Perimetry.Headset
{
    /// <summary>
    /// Sends the HTTP requests <see cref="PortalSync"/> asks for (docs/SYNC_API.md) with UnityWebRequest and
    /// feeds the answers back. Runs in LateUpdate, after <see cref="PerimetryApp"/> has ticked the flow, so a
    /// snapshot always describes the frame just rendered. The exam never waits for the network.
    /// </summary>
    public sealed class PortalSyncBehaviour : MonoBehaviour
    {
        private PortalSync _sync;
        private PatientFlow _flow;
        private int _timeoutS = 8;

        public PortalSync Sync => _sync;

        public void Init(PortalSync sync, PatientFlow flow, int requestTimeoutMs)
        {
            _sync = sync;
            _flow = flow;
            _timeoutS = Mathf.Max(1, requestTimeoutMs / 1000);
        }

        private void LateUpdate()
        {
            if (_sync == null) return;
            var calls = _sync.Tick(Time.realtimeSinceStartupAsDouble * 1000.0, _flow);
            for (int i = 0; i < calls.Count; i++) StartCoroutine(Send(calls[i]));
        }

        private IEnumerator Send(HttpCall call)
        {
            using (var req = new UnityWebRequest(call.Url, call.Method))
            {
                if (call.JsonBody != null)
                {
                    req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(call.JsonBody));
                    req.uploadHandler.contentType = "application/json";
                    req.SetRequestHeader("Content-Type", "application/json");
                }
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Accept", "application/json");
                if (!string.IsNullOrEmpty(call.BearerToken)) req.SetRequestHeader("Authorization", "Bearer " + call.BearerToken);
                req.timeout = _timeoutS;
                yield return req.SendWebRequest();

                if (_sync == null) yield break;
                if (req.result == UnityWebRequest.Result.ConnectionError || req.result == UnityWebRequest.Result.DataProcessingError)
                    _sync.Fail(call.Id, req.error);
                else
                    _sync.Complete(call.Id, (int)req.responseCode, req.downloadHandler.text);
            }
        }
    }
}
