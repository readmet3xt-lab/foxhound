# Underwater transmission on wave32 hosts

Contributed by JokerAttack (foxhound PR #16); related report:
[victory-fork #1368](https://github.com/fxceless/victory-fork/issues/1368).

Under water the lit targets were dark (almost black apart from the interface and
isolated highlights) on NVIDIA cards, whose waves have 32 lanes. Two of the four
volumetric transmission compute programs were refused, so their dispatches were
dropped:

| Log hash | pre.4 on wave32 | With the fix |
|---|---|---|
| `5dc3b25986622949` | valid pipeline | identical translation |
| `6f225b641c5b0708` | valid pipeline | identical translation |
| `926c46bab3b86bd6` | refused although its digest was allowlisted | valid pipeline |
| `a10179ab7eda0dc3` | refused, not allowlisted | valid pipeline |

Both refused programs have one console wave per thread group and share LDS
across the two host waves. A per-host-wave loop retry that still produced
wave64 code cleared the block locations where the halves need to meet, so the
wave-wide fallback found nothing to widen. The retry is now accepted only when
it fits the host's wave (`emit_program`), which keeps those locations for
`widen`. The fallback stays scoped by digest; `a10179ab7eda0dc3`'s digest is
from trustno1-coder/VIctory-NVIDIA#101. `VICTORY_WAVE_WIDE=0` still turns it
off.

## Verification (contributor, RTX 4090)

`victory translate <kernels> --list --pipelines --validate --jobs 1` with
`VICTORY_HOST_MAX_WAVE=32` emitted, validated and created all four compute
pipelines (two on pre.4). In the game the fixed build recorded the submerged
dispatch and its temporal follow-up; its transmission outputs and the lit HDR
target held lighting, exposure stayed finite, and bloom ran without flooding
the frame. Shader tests: 289 passed, 4 ignored.
