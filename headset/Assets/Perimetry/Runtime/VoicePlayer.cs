using UnityEngine;

namespace Perimetry.Headset
{
    /// <summary>
    /// Voice guidance hook (SPEC §8: "guiado por voz e texto"). Plays Resources/Perimetry/Voice/{culture}/{key}
    /// when such a clip exists; the clips themselves are not recorded yet, so for now only the text guides.
    /// </summary>
    public sealed class VoicePlayer
    {
        private readonly AudioSource _source;
        private readonly string _culture;

        public VoicePlayer(GameObject host, string culture)
        {
            _source = host.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0;
            _culture = culture;
        }

        public void Play(string key)
        {
            var clip = Resources.Load<AudioClip>("Perimetry/Voice/" + _culture + "/" + key);
            if (clip == null) return;
            _source.Stop();
            _source.clip = clip;
            _source.Play();
        }
    }
}
