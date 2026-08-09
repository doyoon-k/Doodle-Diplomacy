using System.Collections.Generic;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    /// <summary>
    /// Small, CC0-only sound palette used while the First Contact audio direction is
    /// being evaluated. It attaches sources to the scene-authored Audio root and
    /// deliberately does not create or alter any world geometry.
    /// </summary>
    public enum FirstContactTemporaryAudioClip
    {
        PizzaHouseLoop,
        CarEngineLoop,
        CarDoorClose,
        TelevisionSignalNoise,
        ElevatorDing,
        ElevatorDoor,
        FacilityHum,
        FacilityComputerAmbience,
        MeetingAiry,
        AlienPulse,
        AlienLight,
        TerminalHover,
        TerminalSuccess
    }

    public static class FirstContactTemporaryAudio
    {
        private const string ResourceRoot = "FirstContact/Audio/";

        private static readonly Dictionary<FirstContactTemporaryAudioClip, AudioClip>
            LoadedClips = new();

        public static void Apply(
            FirstContactIntroSceneReferences references,
            FirstContactSecretElevatorSequence secretElevator,
            FirstContactFacilityElevatorArrival facilityElevator)
        {
            if (references == null || references.AudioRoot == null)
            {
                return;
            }

            FirstContactTemporaryAudioRuntime runtime =
                references.AudioRoot.GetComponent<FirstContactTemporaryAudioRuntime>();
            if (runtime == null)
            {
                runtime = references.AudioRoot.gameObject
                    .AddComponent<FirstContactTemporaryAudioRuntime>();
            }

            runtime.Configure(references.Segment);
            secretElevator?.ApplyTemporaryAudioClips(
                LoadClip(FirstContactTemporaryAudioClip.ElevatorDoor),
                LoadClip(FirstContactTemporaryAudioClip.CarDoorClose),
                LoadClip(FirstContactTemporaryAudioClip.ElevatorDoor),
                LoadClip(FirstContactTemporaryAudioClip.FacilityHum));
            facilityElevator?.ApplyTemporaryAudioClips(
                LoadClip(FirstContactTemporaryAudioClip.ElevatorDing),
                LoadClip(FirstContactTemporaryAudioClip.ElevatorDoor));
        }

        public static AudioClip LoadClip(FirstContactTemporaryAudioClip clip)
        {
            if (LoadedClips.TryGetValue(clip, out AudioClip loaded))
            {
                return loaded;
            }

            loaded = Resources.Load<AudioClip>(ResourceRoot + GetResourceName(clip));
            LoadedClips[clip] = loaded;
            return loaded;
        }

        public static void StartSurfaceVehicleLoop()
        {
            FirstContactTemporaryAudioRuntime.GetActive(
                FirstContactIntroSegment.Surface)?.StartVehicleLoop();
        }

        public static void StopSurfaceVehicleLoop()
        {
            FirstContactTemporaryAudioRuntime.GetActive(
                FirstContactIntroSegment.Surface)?.StopVehicleLoop();
        }

        public static void PlaySurfaceTelevisionNoise()
        {
            FirstContactTemporaryAudioRuntime.GetActive(
                FirstContactIntroSegment.Surface)?.PlayTelevisionNoise();
        }

        public static void PlaySurfaceCarDoor()
        {
            FirstContactTemporaryAudioRuntime.GetActive(
                FirstContactIntroSegment.Surface)?.PlayCarDoor();
        }

        public static void StartPizzaMusic()
        {
            FirstContactTemporaryAudioRuntime.GetActive(
                FirstContactIntroSegment.Surface)?.StartPizzaMusic();
        }

        public static void StopSurfaceAudio()
        {
            FirstContactTemporaryAudioRuntime.GetActive(
                FirstContactIntroSegment.Surface)?.StopAll();
        }

        public static void StartMeetingAmbience()
        {
            FirstContactTemporaryAudioRuntime.GetActive(
                FirstContactIntroSegment.Facility)?.StartMeetingAmbience();
        }

        public static void StartFacilityAmbience()
        {
            FirstContactTemporaryAudioRuntime.GetActive(
                FirstContactIntroSegment.Facility)?.StartFacilityAmbience();
        }

        private static string GetResourceName(FirstContactTemporaryAudioClip clip)
        {
            return clip switch
            {
                FirstContactTemporaryAudioClip.PizzaHouseLoop =>
                    "pizza_synthwave_house_loop",
                FirstContactTemporaryAudioClip.CarEngineLoop => "car_engine_loop",
                FirstContactTemporaryAudioClip.CarDoorClose => "car_door_close",
                FirstContactTemporaryAudioClip.TelevisionSignalNoise => "tv_signal_noise",
                FirstContactTemporaryAudioClip.ElevatorDing => "elevator_ding",
                FirstContactTemporaryAudioClip.ElevatorDoor => "elevator_door",
                FirstContactTemporaryAudioClip.FacilityHum => "facility_hum",
                FirstContactTemporaryAudioClip.FacilityComputerAmbience =>
                    "facility_computer_ambience",
                FirstContactTemporaryAudioClip.MeetingAiry => "meeting_airy",
                FirstContactTemporaryAudioClip.AlienPulse => "alien_pulse",
                FirstContactTemporaryAudioClip.AlienLight => "alien_light",
                FirstContactTemporaryAudioClip.TerminalHover => "terminal_hover",
                FirstContactTemporaryAudioClip.TerminalSuccess => "terminal_success",
                _ => string.Empty
            };
        }
    }

    [DisallowMultipleComponent]
    public sealed class FirstContactTemporaryAudioRuntime : MonoBehaviour
    {
        private static readonly Dictionary<FirstContactIntroSegment,
            FirstContactTemporaryAudioRuntime> ActiveRuntimes = new();

        private FirstContactIntroSegment _segment;
        private AudioSource _vehicleLoop;
        private AudioSource _pizzaMusic;
        private AudioSource _televisionNoise;
        private AudioSource _carDoor;
        private AudioSource _facilityHum;
        private AudioSource _facilityComputerAmbience;
        private AudioSource _meetingAmbience;

        public static FirstContactTemporaryAudioRuntime GetActive(
            FirstContactIntroSegment segment)
        {
            if (!ActiveRuntimes.TryGetValue(segment, out var runtime) ||
                runtime == null)
            {
                return null;
            }

            return runtime;
        }

        public void Configure(FirstContactIntroSegment segment)
        {
            _segment = segment;
            ActiveRuntimes[segment] = this;

            if (segment == FirstContactIntroSegment.Surface)
            {
                ConfigureLoop(
                    ref _vehicleLoop,
                    FirstContactTemporaryAudioClip.CarEngineLoop,
                    0.055f);
                ConfigureOneShot(ref _televisionNoise);
                ConfigureOneShot(ref _carDoor);
                return;
            }

            ConfigureLoop(
                ref _facilityHum,
                FirstContactTemporaryAudioClip.FacilityHum,
                0.07f);
            ConfigureLoop(
                ref _facilityComputerAmbience,
                FirstContactTemporaryAudioClip.FacilityComputerAmbience,
                0.045f);
        }

        public void StartVehicleLoop()
        {
            if (_segment == FirstContactIntroSegment.Surface &&
                _vehicleLoop != null && !_vehicleLoop.isPlaying)
            {
                _vehicleLoop.Play();
            }
        }

        public void StopVehicleLoop()
        {
            _vehicleLoop?.Stop();
        }

        public void PlayTelevisionNoise()
        {
            if (_televisionNoise == null)
            {
                return;
            }

            _televisionNoise.PlayOneShot(
                FirstContactTemporaryAudio.LoadClip(
                    FirstContactTemporaryAudioClip.TelevisionSignalNoise),
                0.025f);
        }

        public void PlayCarDoor()
        {
            if (_carDoor == null)
            {
                return;
            }

            _carDoor.PlayOneShot(
                FirstContactTemporaryAudio.LoadClip(
                    FirstContactTemporaryAudioClip.CarDoorClose),
                0.2f);
        }

        public void StartPizzaMusic()
        {
            if (_segment != FirstContactIntroSegment.Surface)
            {
                return;
            }

            ConfigureLoop(
                ref _pizzaMusic,
                FirstContactTemporaryAudioClip.PizzaHouseLoop,
                0.035f);
            if (_pizzaMusic != null && !_pizzaMusic.isPlaying)
            {
                _pizzaMusic.Play();
            }
        }

        public void StartMeetingAmbience()
        {
            if (_segment != FirstContactIntroSegment.Facility)
            {
                return;
            }

            ConfigureLoop(
                ref _meetingAmbience,
                FirstContactTemporaryAudioClip.MeetingAiry,
                0.04f);
            if (_meetingAmbience != null && !_meetingAmbience.isPlaying)
            {
                _meetingAmbience.Play();
            }
        }

        public void StartFacilityAmbience()
        {
            if (_segment != FirstContactIntroSegment.Facility)
            {
                return;
            }

            if (_facilityHum != null && !_facilityHum.isPlaying)
            {
                _facilityHum.Play();
            }

            if (_facilityComputerAmbience != null &&
                !_facilityComputerAmbience.isPlaying)
            {
                _facilityComputerAmbience.Play();
            }
        }

        public void StopAll()
        {
            _vehicleLoop?.Stop();
            _pizzaMusic?.Stop();
            _televisionNoise?.Stop();
            _carDoor?.Stop();
            _facilityHum?.Stop();
            _facilityComputerAmbience?.Stop();
            _meetingAmbience?.Stop();
        }

        private void OnDisable()
        {
            StopAll();
        }

        private void OnDestroy()
        {
            if (ActiveRuntimes.TryGetValue(_segment, out var active) &&
                active == this)
            {
                ActiveRuntimes.Remove(_segment);
            }
        }

        private void ConfigureLoop(
            ref AudioSource source,
            FirstContactTemporaryAudioClip clip,
            float volume)
        {
            source ??= CreateSource();
            source.clip = FirstContactTemporaryAudio.LoadClip(clip);
            source.loop = true;
            source.volume = volume;
        }

        private void ConfigureOneShot(ref AudioSource source)
        {
            source ??= CreateSource();
            source.loop = false;
        }

        private AudioSource CreateSource()
        {
            AudioSource source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;
            return source;
        }
    }
}
