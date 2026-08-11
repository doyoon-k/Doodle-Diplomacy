# Audio Routing Policy

All game audio must pass through `GameAudioMixer` and one explicit category
bus:

- `Music`: authored music and diegetic music such as the pizza shop track.
- `Ambience`: room tone, machinery hum, television noise, and environmental
  beds.
- `SFX`: world interactions, vehicles, doors, elevators, and gameplay effects.
- `UI`: menu, HUD, and terminal feedback.
- `Voice`: spoken dialogue and voice-over.

`Master` is the parent volume control and is not a content category.

## Authoring rules

- Add `GameAudioSourceRoute` to scene- or prefab-authored `AudioSource`
  components and choose the category.
- Immediately call `GameAudio.Route(source, bus)` after creating an
  `AudioSource` at runtime.
- An unassigned source is routed to `SFX` when its scene loads. This is a
  safety fallback, not a substitute for choosing the correct category.
- Use `AudioSource.volume` and `PlayOneShot` volume only as a per-clip trim.
  Use `GameAudio.SetVolume` for player or system-wide relative volume changes.
- Keep spatialization independent from routing: world sounds may be 3D while
  UI sounds are normally 2D, but both still use a mixer bus.

The mixer faders start at 0 dB so introducing routing does not stack new
attenuation on top of the temporary clip trims. Balance passes can be done per
bus without revisiting every playback call.
