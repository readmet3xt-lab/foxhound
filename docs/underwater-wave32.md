# Underwater transmission on wave32

[victory-fork #1368](https://github.com/fxceless/victory-fork/issues/1368)
reported intact G-buffer geometry but dark lit targets underwater, with glare
from the remaining highlights. The local pre.4 log also showed missing
transmission dispatches. Bloom and exposure are downstream; neither is changed
by this fix.

| Log hash | Name in the local 1.0.2.1 shader archive | pre.4 on wave32 | With the fix |
|---|---|---|---|
| `5dc3b25986622949` | `CS_TransmissionVolumetrics_Tiled` | Valid PSO | Identical translation |
| `6f225b641c5b0708` | `CS_TransmissionVolumetrics_Tiled_LocalLights` | Valid PSO | Identical translation |
| `926c46bab3b86bd6` | `CS_TransmissionVolumetricsSubmerged` | Refused despite being allowlisted | Valid PSO |
| `a10179ab7eda0dc3` | `CS_TransmissionVolumetrics_VolumePortalsOnly` | Refused, not allowlisted | Valid PSO |

The last two names are swapped in the issue's comments; the log hashes
disambiguate them. [VIctory-NVIDIA #101](https://github.com/trustno1-coder/VIctory-NVIDIA/pull/101)
provides the missing digest for `a10179ab7eda0dc3`. No unrelated digests from
the PR are needed here. Both kernels have one console wave per thread group
and share LDS across the two host waves. A per-host-wave loop retry that
still produced wave64 DXIL used to clear the block locations where the
halves need to meet. Keeping those locations gives the existing guest
wave-wide fallback the branches it needs to widen. The default fallback
remains digest-scoped, and the explicit `VICTORY_WAVE_WIDE=0` override still
applies.

## Verification

On Windows with an RTX 4090, `victory translate <local-kernels-dir> --list
--pipelines --validate --jobs 1` and `VICTORY_HOST_MAX_WAVE=32` emitted,
validated and created all four compute PSOs (two on pre.4). The inputs can
be extracted from a local archive with `victory shader-info`, setting
`VICTORY_SHADER_ONLY` to the four comma-separated log hashes and
`VICTORY_SHADER_SAVE` to a directory **outside** the checkout.

Two runs loaded copies of the same diving mission checkpoint (Series X,
DLSS Quality, frame generation off, same lighting settings and pass list).
Pre.4 dropped one submerged dispatch per frame in the dive; the picture
was almost black apart from the HUD and isolated bright details. The fixed
build recorded the submerged dispatch and its temporal follow-up. Its
853×480 transmission outputs and 1706×960 lit HDR target held nonzero
lighting, and both the swimmer and the water were visible. One 1706×960
HDR target had nonzero color at 0.75% of pixels on pre.4 versus 100% in
the fixed captures; these are different frames of the same dive, not a
pixel-exact comparison. The exposure pass produced finite values in two
underwater captures, and the bloom downsample and upsample chain ran and
produced nonzero output without flooding the presented frame. These runs
reproduced the dark variant of the corruption, not a full-white frame.
