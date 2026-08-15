using UnityEngine;

namespace Manager
{
    // Background music + button click SFX (backlog item 11, session 11) - Thomas added
    // both audio files under Assets/Resources/Music & Sound/ but nothing ever wired
    // them up. Manager Mode is a single Unity scene throughout (every screen is a
    // SetActive-toggled panel, not a scene load - confirmed across every other system
    // in this file), so a plain component that lives for the app's whole lifetime is
    // enough here - no DontDestroyOnLoad/persistent-singleton machinery needed.
    public class ManagerAudio : MonoBehaviour
    {
        private const string MusicVolumePreferenceKey = "TFM.MusicVolume";
        private const string MusicEnabledPreferenceKey = "TFM.MusicEnabled";
        private const float DefaultMusicVolume = 0.35f;
        private static ManagerAudio instance;

        private AudioSource musicSource;
        private AudioSource clickSource;

        // Called once from ManagerPrototypeController.Start() - host is that same
        // GameObject, so the AudioSources ride along with it for the app's lifetime.
        public static void Initialize(GameObject host)
        {
            if (instance != null)
            {
                return;
            }

            instance = host.AddComponent<ManagerAudio>();
            instance.SetUpAudio();
        }

        private void SetUpAudio()
        {
            AudioClip musicClip = Resources.Load<AudioClip>("Music & Sound/anton_vlasov-slow-trap-18565");
            AudioClip clickClip = Resources.Load<AudioClip>("Music & Sound/buttonclicksound");

            musicSource = gameObject.AddComponent<AudioSource>();
            musicSource.clip = musicClip;
            musicSource.loop = true;
            musicSource.playOnAwake = false;
            // Background level, not competing with the click SFX or any future voice/
            // commentary - deliberately quiet ambience rather than a foreground track.
            musicSource.volume = Mathf.Clamp01(PlayerPrefs.GetFloat(MusicVolumePreferenceKey, DefaultMusicVolume));
            musicSource.mute = PlayerPrefs.GetInt(MusicEnabledPreferenceKey, 1) == 0;

            // Deliberately NOT started here - Thomas wanted the studio splash to play in
            // silence, with music only starting once Title actually appears. See
            // PlayMusic, called from ManagerPrototypeController.AdvanceFromSplashToTitle.
            if (musicClip == null)
            {
                Debug.LogWarning("ManagerAudio: background music clip not found at Resources/Music & Sound/anton_vlasov-slow-trap-18565.");
            }

            clickSource = gameObject.AddComponent<AudioSource>();
            clickSource.clip = clickClip;
            clickSource.loop = false;
            clickSource.playOnAwake = false;
            clickSource.volume = 0.6f;

            if (clickClip == null)
            {
                Debug.LogWarning("ManagerAudio: click SFX clip not found at Resources/Music & Sound/buttonclicksound.");
            }
        }

        // Starts the background loop - called once Title actually appears (after the
        // studio splash fades out), not at launch. Guarded so it's safe to call from
        // both AdvanceFromSplashToTitle (first launch) and anywhere else that might
        // reach Title later without risking restarting an already-playing track.
        public static void PlayMusic()
        {
            if (instance != null && instance.musicSource != null && instance.musicSource.clip != null && !instance.musicSource.isPlaying)
            {
                instance.musicSource.Play();
            }
        }

        // PlayOneShot (not Play) so rapid clicks layer/restart cleanly instead of
        // cutting each other off mid-sound.
        public static void PlayClick()
        {
            if (instance != null && instance.clickSource != null && instance.clickSource.clip != null)
            {
                instance.clickSource.PlayOneShot(instance.clickSource.clip);
            }
        }

        // Settings screen music toggle (backlog item, session 12). Mute rather than
        // Stop/Play - keeps the loop's playback position intact so turning music back on
        // resumes where it left off instead of restarting the track. Click SFX is
        // deliberately unaffected - the ask was specifically "music on/off", not a master
        // mute.
        public static void SetMusicEnabled(bool enabled)
        {
            PlayerPrefs.SetInt(MusicEnabledPreferenceKey, enabled ? 1 : 0);
            PlayerPrefs.Save();
            if (instance != null && instance.musicSource != null)
            {
                instance.musicSource.mute = !enabled;
            }
        }

        public static bool IsMusicEnabled()
        {
            return instance == null || instance.musicSource == null || !instance.musicSource.mute;
        }

        public static void SetMusicVolume(float volume)
        {
            float clamped = Mathf.Clamp01(volume);
            PlayerPrefs.SetFloat(MusicVolumePreferenceKey, clamped);
            PlayerPrefs.Save();

            if (instance != null && instance.musicSource != null)
            {
                instance.musicSource.volume = clamped;
            }
        }

        public static float GetMusicVolume()
        {
            if (instance != null && instance.musicSource != null)
            {
                return instance.musicSource.volume;
            }

            return Mathf.Clamp01(PlayerPrefs.GetFloat(MusicVolumePreferenceKey, DefaultMusicVolume));
        }
    }
}
