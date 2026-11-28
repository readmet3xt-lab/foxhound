# foxhound

A fork of [VIctory](https://github.com/Marvisak/VIctory), a compatibility layer that runs an Xbox Series title
natively on Windows from files **you** supply. foxhound is tuned for **NVIDIA RTX** GPUs: DLSS Super Resolution and
DLSS Frame Generation in place of the title's own upscaling, the Series X profile at a native 2560x1440, and fewer
stalls at scene cuts.

> **This repository contains no game files, code or assets.** You need your own legally obtained copy of the
> title. foxhound is not affiliated with or endorsed by the title's developer or publisher, nor by NVIDIA.
> See [docs/asset-policy.md](docs/asset-policy.md).

## Quick start: the ready-made program

No tools, no build, no AI assistant. You need your own copy of the title and about 10 minutes.

1. Download `foxhound-windows-x64.zip` from the [latest release](https://github.com/readmet3xt-lab/foxhound/releases)
   and unpack it anywhere. It holds `Foxhound.exe` (the launcher), `victory.exe` (the layer), a README and the
   licence. Nothing of the title and nothing of NVIDIA's is in it.
2. Start `Foxhound.exe`. Windows may warn about an unknown publisher (the programs are not signed): "More info",
   then "Run anyway". The release notes give each file's SHA-256, and you can build both programs yourself
   (below).
3. The first start asks once for **the folder that holds your copy of the title** (the folder with its `.exe`;
   the folder above it is found too) and puts a shortcut "Foxhound" on the desktop.
4. Choose your settings and press **Play**:

   | Setting | Choices |
   |---|---|
   | Resolution | 1080p class (Series S profile: lightest, no DLSS), 1440p (Series X profile, the tested one), 4K (Series X profile) |
   | DLSS | Off, DLAA, Quality, Balanced, Performance (NVIDIA; needs the Streamline folder, see *Set up*) |
   | Frame Generation | Off / On (RTX 40 or later, with DLSS) |
   | Display | Fullscreen (borderless), borderless window, window with a title bar |
   | VRAM limit | Auto (what Windows reports for your card), 6 to 24 GB |
   | Texture loading | Auto, all detail loaded (12 GB or more), detail on demand (less VRAM, can blur) |
   | Dark areas | how much of the indirect-light estimate is used (ray tracing is off) |
   | Frame rate | 30 fps (the title's own), 40, 60 (see *What frame rate will I get?*) |
   | Process priority, Log, further switches | as named |
   | Layer build | Release, or Diagnostics if you built `victory-diag.exe` and put it beside the launcher: F2 then saves the frame being drawn for a bug hunt |

5. Behind a loading screen the launcher translates the title's shaders (2 to 5 minutes, once per version of
   foxhound) and compiles the pipelines earlier runs asked for (seconds; minutes after an update of foxhound or
   of the graphics driver). Then the game starts.
6. While playing, **F1** opens the settings. Each one says whether it takes effect "at once" or "after restart";
   "Restart the game" and "Quit the game" are in the same menu.

**The very first run is slow, once.** A scene shown for the first time on your PC compiles its pipelines while
you play: for about a minute each the picture runs at a few frames a second and objects (faces too) are missing.
Every pipeline is written down; from the second start on the launcher compiles them all on the loading screen.
The list cannot be shipped: it is made from your copy of the title.

### Build it yourself instead

You need about 30 minutes and 10 GB of free disk space.

1. **Install the tools** (once). In a PowerShell window:

   ```powershell
   winget install Git.Git
   winget install Rustlang.Rustup
   winget install Microsoft.VisualStudio.2022.BuildTools --override "--add Microsoft.VisualStudio.Workload.VCTools --includeRecommended --passive"
   ```

   Close the window and open a new one, so that `git` and `cargo` are found.

2. **Get foxhound and build the release folder** (10-15 minutes the first time):

   ```powershell
   git clone https://github.com/readmet3xt-lab/foxhound
   cd foxhound
   .\scripts\build-launcher.ps1 -Package
   ```

   If PowerShell answers that running scripts is disabled, allow your own scripts once with
   `Set-ExecutionPolicy -Scope CurrentUser RemoteSigned` and start again. The folder
   `target\foxhound-windows-x64` is what the release's .zip holds: start `Foxhound.exe` in it.

Without the launcher: `cargo build --release --locked -p victory`, then `scripts\play-foxhound.ps1 -Exe <your
copy's folder or .exe>` (see *Run*).

## Where it stands

Tested on one PC: Windows 11, Ryzen 7 7800X3D, 32 GB RAM, **RTX 4070 SUPER 12 GB**, NVMe SSD; the Xbox Series
build **v1.0.2.1** of the title, Series X profile, 2560x1440 output, DLSS Quality, no Frame Generation. The
numbers are one scripted run through the launcher from the menu through the first boat ride and 20 minutes of
driving (26 minutes in all), the window's frame time read once a second, with every pipeline compiled before
the run (a second start, not a first). 30 fps (33.3 ms) is the title's own limit.

| | Measured (2026-11-28) |
|---|---|
| 20 minutes of driving | median 33.3 ms, mean 34.4 ms; 97 of 1,175 seconds at 40 ms or more, the worst 50 ms (before this release, minutes 18 to 24: 47 to 51 ms) |
| Opening cutscenes | median 33.4 ms, mean 36.4 ms; 47 of 256 seconds at 40 ms or more |
| First 40 s of the opening | mean 36.8 ms (pre.1: 171 ms, objects missing, on every start) |
| Start of a scene | its first seconds at 40-55 ms while it streams in; the first scene's first second up to 0.4 s a frame |
| Heavier scenes past the boat ride | one played session: four minutes at 16 to 21 fps with 8,000 draws a frame (before this release) |
| Memory | flat for 25 minutes: 27 GB committed (keep the page file on), 14 to 21 GB of RAM in use, 11.7 GB of VRAM on the 12 GB card. Before this release: 25 GB growing to 34 GB in 20 minutes |

These are pre.3's. pre.4 was measured in shorter runs only (9 minutes through the launcher, three 7-minute
diagnostics runs): opening cutscenes 36.0 ms mean (pre.3: 36.3), the first minute of driving 38.7 ms (40.2),
committed memory about 0.8 GB lower at the same point, no difference in the pictures. pre.5 was tested in
short runs (6 to 10 minutes): no change in frame time on the Series X profile; on the Series S profile with a 6 GB
limit, DLSS Quality and Frame Generation, median 27.9 fps with dips to 16 to 22 in the heaviest opening shots.

DLSS mode against cutscene frame time (same run, before the last fix; gameplay is 33.3 ms in all three):

| Mode | Mean | Seconds at 40 ms or more (of 256) |
|---|---|---|
| Quality | 36.8 ms | 61 |
| Balanced | 36.5 ms | 39 |
| Performance | 35.3 ms | 32 |

**It is not a stable 30 fps yet.** Where the frame rate falls, the card is idle a third of the time: one thread
of the layer (it prepares and submits every frame's work) is the limit, and in heavy scenes most of its time
goes to copying the title's changed buffers (17 to 25 ms a frame at 8,000 draws). That is the work ahead; 60 fps
needs the same thread at half of what it costs in the lightest gameplay today (26 ms).

What is known to be wrong:

- **No ray tracing.** The title traces its indirect light, reflections and lamp shadows in its own compute
  shaders. They are off (`VICTORY_BVH_BUILDER=1` turns them on: about 3 fps and a broken picture today). An
  estimate stands in for indirect light and for reflections on opaque surfaces and glass: rooms are lit,
  reflections are blurry, dark places can be too dark (launcher: "Dark areas"). **Water reflects the sky only**,
  not boats, islands or the underside of a bridge.
- **First run:** every scene compiles its pipelines the first time it is shown on your PC: for about a minute the
  picture runs at a few frames a second and objects, faces among them, are missing. Once per scene. After an
  update of foxhound or of the graphics driver, or from a new folder, the driver compiles them all again on the
  loading screen (16 minutes for 88,604 pipelines here; "Skip" starts the game without waiting).
- **Start of a new game:** the picture stands still for 1 to 2 s at the first 3D picture (4 s before this
  release), a few frames of another scene show after it, and the first shot is too dark for about 4 s.
- **Reported, not reproduced here:** an all-white picture with only the HUD at 10 fps, and a bright white glare
  at night (both may have been the memory leak fixed below; tell us if they still show); objects that show or
  vanish for an instant; pixelated faces in some cutscenes; black patches on water around floating objects
  (first public build).
- **Left out:** the heat-haze / in-scattering pass (it smears blocks of pixels, also on water) and the decal
  heap's defragmentation (5.6 ms of GPU a frame). `VICTORY_LEAVE_OUT` names them.
- **Frame rate:** see above. The launcher's 40 and 60 fps settings tell the title to run faster; the layer does
  not reach them. DLSS Frame Generation shows twice the frames the title renders.
- **Reported in play past the first boat ride:** water's surface texture too soft; flicker at the draw distance;
  objects that load only when near (with under 3 GB of RAM free the layer throttles its texture prefetch: close
  other programs).
- Nothing past the first boat ride is tested by us. Frame-generation tools that work on the window from outside
  (Lossless Scaling) see uneven frame times and look unstable.

Fixed since the first public build:

- **8 GB cards: black faces and black or missing textures** (issues #5, #15, #19): the launcher's "Auto" texture
  loading took detail levels on demand under 11 GB; on a full card those stayed blurry or missing (a test with a
  6 GB limit: 19,600 textures waiting, the opening's models missing). Auto now loads every level. And a texture
  the card has no room for is made in the PC's memory, which the card reads over the bus, instead of staying
  black until room comes (`VICTORY_TEXTURES_IN_PC=0`: as before). In the same test the faces kept their colour
  and the frame rate matched the run without it (median 27.9 fps, Series S profile, DLSS Quality and Frame
  Generation).
- **DLSS and Frame Generation on the Series S profile** (the setting for cards under 12 GB), and 3x / 4x Multi
  Frame Generation on RTX 50 cards (untested here). Frame Generation now gets the title's real frame timing
  (latency markers where the title starts and ends a frame).
- **Profiles** (issue #11): up to four players on one PC, each with saves and settings of their own (launcher:
  "Profile").
- **Cutscene black on a first start** (issue #19): the scene's last whole picture stays while its pipelines are
  compiled (at most two minutes; `VICTORY_FIRST_HOLD=0` turns it off).
- **Wrong colours baked into clothes or faces** with `VICTORY_THIN=1` (issue #17; edsuuu and MrEjecutor):
  bake draws are translated at the title's call (`VICTORY_THIN_BAKE=2`).
- **Underwater transmission on wave32 cards** (PR #16 by JokerAttack).
- **Frame recorder** (issue #18): a capture is given up after `VICTORY_RECORD_LIMIT_S` (20 s) and the game goes on.
- **Guards lock** (issue #8): write faults no longer take it (`VICTORY_FAULT_UNLOCKED=1`).
- **Refused memory** (issue #9): the log names every resource or heap the title could not get.

- **DLSS greyed out on a PC with integrated graphics** (issue #14): the launcher took the card with the largest
  memory figure in the registry, which an integrated GPU can inflate, and the game made its device on Windows'
  first adapter, which can be the integrated one. The launcher, the warm-up, the game and DLSS now all take the card
  with the most memory of its own (`VICTORY_ADAPTER` names another).
- **Slower and slower over a long session on cards under 12 GB** (issue #12): each look for textures to let go
  went again through every texture already let go: 3 ms a look after a minute, 14 ms after 30 on a 10 GB card.
  Those are passed by now.
- **Crashes from Windows running out of memory**: the launcher warns before Play when RAM and page file are
  under 52 GB together, and the helper lists of the graphics driver's command lists are pooled (about 190 alive
  instead of about 1,100).
- **Memory leak in long play** (reports: 5 fps after 10 minutes, lag and stuttering sound): the title leaves
  leftover bytes in unused fields of the views it places in memory, so the layer took most of them for new ones
  and kept them all: about 3 GB more every 20 minutes, frame time past 45 ms. The unused fields are cleared now;
  in a 25-minute run memory stays flat and the frame time stays at 33 to 34 ms.
- **Squares of clear sky in clouds** for a second or two after a cut: the cloud pass's sort of its tiles lost
  about one tile in eleven (a group-wide wait stood in loops that each wave of the card takes on its own).
- **Violet vegetation**: distant trees and shore plants were tinted violet (whole canopies in some reports). The
  light estimate took violet leftovers of the title's probes for light; they are dropped now (Ahyildiz's fix,
  after Belobug). Green again in the shots tested here; tell us where it still shows.
- **The stand-still at the first 3D frame** is shorter: the 3,600 small buffers it asks for are made ahead.
- Dark rectangles on water beside boats, people and islands; dark slabs along the horizon and where rocks,
  plants or hulls meet the water; single dark tiles that flickered along the horizon (three faults in how
  64-lane compute programs are run on NVIDIA's 32-lane waves).
- Bright sky-coloured reflections in the opening's dark rooms, also on glass and spectacles.
- Shader stutter after "building the shaders": the launcher prepares them itself, with the run's own switches.
- **Magenta or violet casts** on surfaces lit mostly by the screen probes (papers on a wall, irises): the probes' ray activation pass was left out, so the probes kept stale values. It runs
  now (`VICTORY_ACTIVATE_RAYS`); found and measured by a tester (issue #2).
- **A crash a few seconds after start on Windows 10** (RTX 2080 Ti, RTX 2070 reports) is now a plain message
  (see "What you need").
- **"The title ended before its window appeared (code 1)"** with a changed executable (an FPS patch): the
  launcher now shows why, and names the unchanged copy beside it (`.bak`) when there is one (issue #6).
- **Slow scene starts on every run** (3 to 9 fps for the first 30 s of the opening, objects missing), also after
  the loading screen had compiled everything: the driver stores a compiled pipeline under the order in which the
  process made its static samplers, and that order differed from run to run, so the driver compiled the same
  pipelines again each time. The samplers are now made in one fixed order in every process
  (`VICTORY_SAMPLER_ORDER`). After this update the loading screen compiles every pipeline once more.
- "Access is denied (os error 5)" when the title's folder was given in place of its executable.
- Ported from other forks (see *Credits*): BC7 textures with rotation bits decoded right, raw buffer views kept
  inside their buffers, descriptor slots given back to the right heap.

## Questions people asked

**Is ray tracing on or off?** Off. The title does its ray tracing (indirect light, reflections, lamp shadows) in
its own compute shaders, which this layer cannot run fast enough yet. An estimate stands in for indirect light and
for reflections on opaque surfaces and glass. Water reflects the sky only; it does not mirror boats or islands yet.

**Is DLSS on?** With Streamline set up, DLSS Super Resolution (Quality) replaces the title's own upscaler by
default; the launcher has the other modes (Balanced and Performance take load off the card: fewer slow seconds
in cutscenes, see the table above). Frame Generation is off unless you turn it on. DLSS gets the title's own
motion vectors and depth: the layer answers the title's FidelityFX upscaler call with DLSS.

**I built the shaders and it still stutters at every new scene.** Two causes, both fixed in the launcher now.
(1) A translation is only found again by a run with the same switches; the older instructions translated with
other switches than the launcher sets, so the run translated everything again while playing. (2) The GPU driver
compiles a pipeline the first time it is drawn; the launcher now compiles every pipeline earlier sessions used
before the game starts. A scene you enter for the first time ever can still stutter once.

**It gets smoother after a few launches. Why?** That is (2) above: each session writes down the pipelines it
needed, and the next start compiles them ahead.

**Can it do 60 fps?** Not yet as rendered frames: the layer needs 26 ms a frame in the lightest gameplay. What
works today is DLSS Frame Generation (launcher: on), which shows 60 from 30 rendered, with NVIDIA Reflex on. The
launcher's 40 and 60 fps settings change what the title asks for; they help only where the layer is faster than
33 ms.

**The game looks like slow motion.** The title advances its world by a fixed step per frame and expects 30 frames
a second. Below 30 fps everything, sound aside, runs slower than real time.

**What frame rate will I get?** One CPU thread of the layer limits the frame rate in most scenes, so a CPU with
fast cores and a large cache matters more than the GPU. Reports from the first public build (not measured by us):

| GPU | CPU | RAM | Reported |
|---|---|---|---|
| RTX 4070 Ti | Ryzen 7 9800X3D | 48 GB | "nice frames", playable |
| RTX 4070 SUPER | Ryzen 7 7800X3D | 32 GB | 30 fps (this is the PC it is tuned on) |
| RTX 4070 SUPER | Ryzen 7 5800X3D | 32 GB DDR4 | about 30 fps, frequent stutter |
| RTX 4070 SUPER | Core i5-12600KF | 32 GB DDR4 | 10-15 fps in the prologue, 30 in the opening |
| RTX 4090 | not given | 64 GB | below 30 half of the time, some stutter |
| RTX 3080 12 GB | Ryzen 7 5700X | 32 GB DDR4 | about 15 fps |
| RTX 2080 SUPER | Ryzen 5 2600 | not given | about 12 fps |
| RX 9060 XT 16 GB | Ryzen 5 7500F | 32 GB DDR5 | 10-25 fps |

**I have an AMD card.** It runs, but this fork's choices are made for NVIDIA cards. Try
[Belobug/VIctory-Belobug-AMD](https://github.com/Belobug/VIctory-Belobug-AMD), which is tuned on AMD cards.

**Does the game save?** Yes. Saves are in `%LOCALAPPDATA%\victory\data` and are kept between builds.

**Is this safe to run?** The release's two programs are built from this source by `scripts\build-launcher.ps1
-Package`; the release notes give their SHA-256, and you can build them yourself and compare what they do. They
are not signed, so Windows SmartScreen warns once. Neither goes online. They read your copy of the title and
write caches, logs and saves under `%LOCALAPPDATA%\victory` and settings and logs under
`%LOCALAPPDATA%\foxhound`; the launcher also makes one desktop shortcut. Nothing else.

**"My antivirus flags Foxhound.exe."** The launcher of 0.2.0-pre.2 was flagged by 3 of 68 engines on
VirusTotal (Symantec ML.Attribute.HighConfidence, Bkav and SecureAge: machine-learning and generic verdicts). It
did things malware also does: it read F1 and F2 while another program was in front, forced its menu to the front,
changed another program's window, made the desktop shortcut through the Windows Script Host and ended processes
with `taskkill`. Since 0.2.0-pre.3 the game's own window handles F1, F2 and its display mode, and the launcher does
none of those things. Neither program is signed (a code-signing certificate costs money every year). The release
lists the SHA-256 of every file, and the source builds both programs with one script, so you can compare.

**"Error: reading ...: Access is denied. (os error 5)".** `victory run` was given a folder where it wanted the
title's `.exe` (Windows answers a read of a folder that way). Since 2026-11-27 a folder is accepted and the
executable in it is used; the launcher asks for the folder only.

**I get 10 to 15 fps on a card like yours.** Three things to check. (1) The first run compiles pipelines while
you play (see *Quick start*): judge the second run. (2) The switch set matters: started with plain `victory run`
or another fork's script, most of what makes this fork fast is off. The launcher sets all of it. (3) Under 32 GB
of RAM or under 12 GB of VRAM the Series X profile swaps: choose "1080p class". If it is still slow, open an
issue with the newest file of `%LOCALAPPDATA%\foxhound\logs` (launcher: "Logs").

**Where do I get the title?** From your own console and your own purchase. No game files are here, none are
linked, and requests for them are closed without an answer.

## What you need

- **Windows 11** (x64), a CPU with AVX2, 32 GB of RAM or more, an SSD. Windows 10's own Direct3D 12 lacks the
  Shader Model 6.6 descriptor heaps that every translated shader uses: there the program says so and stops.
- **RAM and page file together at least 52 GB**, or a page file whose size Windows manages (the default). While
  the game runs the test PC commits about 48 GB in all; past Windows' limit the title stops ("Failed to
  allocate ..."). The launcher warns before Play when the page file is off or too small, and says how to fix it.
- An NVIDIA GeForce RTX GPU with a recent driver for DLSS (RTX 40 or later for Frame Generation, with
  hardware-accelerated GPU scheduling on). Any Direct3D 12 GPU with shader model 6.6 and mesh shaders runs the
  layer without DLSS.
- **Your own dump** of the title's Xbox Series build v1.0.2.1 (the folder with `game_scarlett_master.exe`).
  Other builds may start, but the image patches (1440p output, FidelityFX path, frame rate) check every byte
  they change and leave a differing build alone.
- [Rust](https://rustup.rs) (the toolchain is pinned in `rust-toolchain.toml`; rustup fetches it), and the
  **Visual Studio Build Tools** with "Desktop development with C++" (MSVC and a Windows SDK).
- For DLSS: NVIDIA's **Streamline SDK** (below). You download it yourself; nothing of NVIDIA's is in this
  repository.

## Build

```powershell
git clone https://github.com/readmet3xt-lab/foxhound
cd foxhound
cargo build --release --locked -p victory
```

The diagnostics build (frame timelines, captures, GPU timing) is optional:
`cargo build --locked --profile diag -p victory --features diagnostics`.

## Set up

1. **Point it at your copy.** Copy `victory.example.toml` to `victory.toml` (git-ignored) and set `game_dir` to
   your dump's folder. Keep your files **outside** this repository; the tools refuse otherwise.

   ```toml
   [paths]
   game_dir = 'D:\Games\MyDump'
   ```

2. **DLSS (optional, recommended on RTX).** Download the Streamline SDK release from
   <https://github.com/NVIDIA-RTX/Streamline/releases> (tested: `streamline-sdk-v2.14.16.zip`; it comes under
   NVIDIA's RTX SDK license, which you accept yourself), unpack it **outside** the repository, and point foxhound
   at its `bin\x64` folder:

   ```powershell
   [Environment]::SetEnvironmentVariable('VICTORY_STREAMLINE_DIR', 'D:\sdk\streamline-sdk-v2.14.16\bin\x64', 'User')
   ```

   Check it without the title: `target\release\victory.exe upscaler-check --device` (and `--present` for Frame
   Generation). Details: [docs/dlss.md](docs/dlss.md). Without Streamline the launcher runs without DLSS.

3. **Nothing else.** The launcher translates the shaders and compiles the pipelines before each start
   (`-PrepareOnly` does only that; `-NoPrepare` skips it).

## Run

`Foxhound.exe` (see *Quick start*) is the way meant for playing. `Foxhound.exe --play` starts at once with the
saved settings; `--game <folder>` sets the game folder. The script below does the same from PowerShell, for
development:

```powershell
.\scripts\play-foxhound.ps1 -Exe 'D:\Games\MyDump\game_scarlett_master.exe'
```

| Option | What |
|---|---|
| *(none)* | Series X profile, 2560x1440, DLSS Quality (if Streamline is set up), all fixes on |
| `-Dlss off` / `dlaa` / `balanced` / `performance` | the title's own upscaling, or another DLSS mode |
| `-FrameGen` | DLSS Frame Generation (about +1.4 GB of VRAM) |
| `-SeriesS` | the Series S profile: 1280x720 inside, faster, softer |
| `-Res 1080to1440` / `4k` | scene at 1080p upscaled by the title / the stock 3840x2160 output |
| `-Build diag` | the diagnostics build |
| `-More @{VICTORY_X='1'}` | add or override any switch |
| `-PrepareOnly` / `-NoPrepare` | only translate shaders and compile pipelines / start without doing so |

Full screen: start the game, then run `.\scripts\fullscreen.ps1` in another window (borderless, the size of the
monitor the game is on).

Logs go to `%LOCALAPPDATA%\victory\logs`, saves to `%LOCALAPPDATA%\victory\data`. Close the game window to quit.

### Shaders and pipelines

Before the game window opens the launcher runs `victory.exe shaders` (the title's shaders translated, once per
build of foxhound and per set of switches: 2-4 minutes) and `victory.exe warm` (every pipeline that earlier sessions
asked for, compiled by the GPU driver: seconds, or minutes after a new build or driver). It runs both with the
switches of the run itself; translations made with other switches are not found by the run. Runs write down every
pipeline the title makes (`VICTORY_PIPELINE_BOOK=1`), so a scene stutters at most the first time it is ever seen.

The NVIDIA driver keeps its compiled pipelines **per program path**: a build run from another folder starts cold.

## Switches worth knowing

Every `VICTORY_...` switch is described in a comment where the code reads it. The launcher's full set is in
`scripts/play-foxhound.ps1`; these are the ones foxhound added or changed:

| Switch | Launcher | What |
|---|---|---|
| `VICTORY_DLSS`, `VICTORY_DLSS_MODE` | `quality` | DLSS Super Resolution through the title's FidelityFX dispatch |
| `VICTORY_FRAMEGEN` | off (`-FrameGen`) | DLSS Frame Generation, with the picture before the HUD tagged |
| `VICTORY_NATIVE_1440P` | on | Series X renders and outputs 2560x1440 (image patched) |
| `VICTORY_SOFT_VIEWS` | `copies` | the title's tiled-heap page mappings kept without Windows views: no freezes at scene cuts |
| `VICTORY_WC_NO_BACK`, `_MIN_KB` | `1`, `1024` | shader writes into big write-combined copies are not copied back |
| `VICTORY_AMBIENT`, `VICTORY_AMBIENT_SPECULAR` | `0.8`, `1` | estimates of indirect light and of reflections (no ray tracing) |
| `VICTORY_EARLY_DEPTH` | on | pixel programs keep the console's early depth test |
| `VICTORY_LOOPS_WAVE` | `1` (in the code) | compute programs that depend on the GPU's waves keep each wave together through their loops (`2`: every compute program, `0`: off); fixes water tiles dropped on NVIDIA |
| `VICTORY_LDS_FIRST_INIT` | on (in the code) | a thread group's first thread always sets up its shared memory (`0`: only if its lane runs, as the console); fixes dark water tiles at edges |
| `VICTORY_DSTORAGE_PRIORITY`, `VICTORY_DSTORAGE_WORKERS` | `-1`, all processors (in the code) | the threads that unpack streamed assets run below normal priority, so the frame's threads get the processors first (`0`: normal, as before) |
| `VICTORY_PLACED_READS` | off | experimental: a fill of constants that reads what a buffer copy earlier in the same submission wrote is done on the card out of that copy's source, instead of waiting about 30 ms for the card. It takes 9 to 14 ms off every frame of the opening's boat cutscene (slow seconds 61 to 28 of 256), but in two of four runs one character's skin was drawn as coloured noise and dark blocks showed on water around boats. A guard for the cause found is in; it is not tested enough to be on |
| `VICTORY_BUFFERS_AHEAD` | 4096 (in the code) | buffers of 64 KiB made by a background thread before the title asks for thousands of them in one frame (`0`: each made when asked for) |
| `VICTORY_VRAM_BUDGET_MB` | launcher: "VRAM limit" | the most video memory the layer plans with (not given: what Windows reports) |
| `VICTORY_LDS_BOUNDS` | on (in the code) | compute programs' reads outside their shared memory give 0 and writes are dropped, as on the console (`0`: as before) |
| `VICTORY_RAW_VIEW_CLAMP` | on (in the code) | raw buffer views stay inside their buffer (from fxceless/victory-fork) |
| `VICTORY_LDS_LOOP_ATOMICS` | on (in the code) | shared-memory atomics in loops do not wait for the whole thread group (`0`: they do, as before): the cloud tiles' sort keeps all its tiles |
| `VICTORY_ACTIVATE_RAYS` | on (in the code) | the screen probes' ray activation pass runs (`0`: left out, as before: magenta and violet casts) |
| `VICTORY_SAMPLER_ORDER` | on (in the code) | static samplers are given to the driver in one fixed order in every process (list in the cache folder), so its shader cache finds the title's pipelines again (`0`: in the order each run makes them, as before) |
| `VICTORY_ADAPTER` | not set | the graphics card to run on, by a part of its name (not given: the card with the most memory of its own; the launcher shows the same one) |
| `VICTORY_TEXSPK_PASS_MS` | launcher: `8` | a frame's runs of the pass over textures picked by index take at most this many ms together; the rest waits a frame (`0`: each run 16 ms on its own). By Dastano (PR #13) |
| `VICTORY_PASSES_HOT` | launcher: `1` | the helper threads are woken at each submission's start (µs they wait awake; `0`: off). By Dastano (PR #13) |
| `VICTORY_PRELUDE_POOL` | on (in the code) | the small lists that run before a list's work come from a shared pool while that work runs (`0`: each list keeps its own: about 1,100 alive instead of about 190) |
| `VICTORY_WIDE_THREADS` | three eighths of the logical processors, 2 to 8 | the threads that copy changed title buffers for a submission (it was a quarter, 2 to 6) |
| `VICTORY_HELPER_PRIORITY` | `1` | the layer's threads that a frame waits for run above normal priority (`0`: normal) |
| `VICTORY_PRELUDE_UPLOADS`, `VICTORY_SHOW_NEW_LATER`, `VICTORY_TABLED_REREAD` | on | texture upload order and slot changes (flicker, device losses) |
| `VICTORY_SYNC_IN_PLACE`, `VICTORY_SPANS_QUICK`, `VICTORY_HEAP_SYNC_AT_SUBMIT` | on | cheaper descriptor-heap bookkeeping |
| `VICTORY_PORT_CONSTANTS` | off | the title's constants heap read in place: less CPU, but on a 12 GB card the GPU then waits (slower overall) |
| `VICTORY_FRAME_INTERVAL_MS` | off | experimental: the title's own 30 fps setting patched to a higher rate |
| `VICTORY_BVH_BUILDER` | off | the title's ray tracing (slow, broken) |

## How it works, and the findings

- [docs/architecture.md](docs/architecture.md): the layer's parts.
- [docs/findings.md](docs/findings.md): measurements behind the choices.
- [docs/dlss.md](docs/dlss.md): DLSS and Frame Generation.
- [docs/fork-ray-tracing.md](docs/fork-ray-tracing.md): the title's ray tracing and the light estimate.
- [docs/replay.md](docs/replay.md): recording a frame and replaying it.
- [docs/asset-policy.md](docs/asset-policy.md): what may and may not be in this repository.

## Contributing

Issues and pull requests are welcome. Never attach or commit anything from the game: files, dumps, shader dumps
or extracted data. Describe faults technically. `cargo xtask policy` (also the pre-commit hook in `.githooks/`)
checks the asset policy.

## Credits

A tree of other people's work with more on top. This repository starts from one commit; the history of the work
before it is in the projects named here, whose authors wrote most of this code.

- **[VIctory](https://github.com/Marvisak/VIctory)** by Marvisak and its contributors: the loader, the runtime
  services, the Direct3D 12 back end and the shader recompiler.
- **[fxceless/victory-fork](https://github.com/fxceless/victory-fork)** and the people whose pull requests it carries.
- **[avatarmanz/VIctory-7900XTX](https://github.com/avatarmanz/VIctory-7900XTX)**: the submission line, write
  guards, relay and much of the speed work this fork starts from.
- **[codec-d/cool_fox](https://github.com/codec-d/cool_fox)**: the base this fork was taken from.
- **[DexNom/VIctory](https://github.com/DexNom/VIctory)**: native 1440p output, the FidelityFX path on Series X,
  ray tracing fixes.
- **[Belobug/VIctory-Belobug-AMD](https://github.com/Belobug/VIctory-Belobug-AMD)**: the light estimate, renderer
  ports (constants heap in place, heap records), texture upload order, the frame rate switch; ported commits name
  their source.
- Fixes taken as source patches, each read line by line: the BC7 rotation fix (fxceless), violet probe leftovers
  dropped from the light estimate (Ahyildiz, after Belobug), raw buffer views kept inside their buffer
  (lichenlang89, by way of CuteBooty/victory-8gb), dynamic descriptor slots given back only to their own heap
  (fxceless/victory-fork#1324), statistics kept out of release builds (LucaruluDev).
- **[FQY55kai/ZJOU-VICTORY](https://github.com/FQY55kai/ZJOU-VICTORY)**, **[myokex/victory-amd](https://github.com/myokex/victory-amd)**
  (with Spexivus, joroyt and tightfall), Joshua Odom, **Geckomacabre** and **cxy0072019-sketch**.
- **[XCL](https://github.com/ivall/xcl)** (GPL-2.0-only) was read for how the same title behaves under another
  layer; no code of it is here.
- NVIDIA Streamline, DLSS and Reflex are NVIDIA's; they are loaded at run time from a folder you supply.
- Much of this fork's code was written with [Claude Code](https://claude.com/claude-code).

## What is and is not here

- **No part of the title is in this repository or its releases**: no files, code, shaders, textures, audio,
  screenshots or footage, and no links to any of them. The places the image patches change are checked against
  the length and BLAKE3 hash of the stock bytes, not against copies of them; shader identifiers in the code are
  hashes and short names the layer looks for in your copy at run time.
- foxhound is a compatibility layer: it reads the files of **your own, legally obtained copy**, translates its
  graphics commands and shaders on your PC while it runs, and keeps the translations in a cache folder on your PC.
  Nothing it makes from your copy is uploaded or shared, and the title's own crash uploads are blocked.
- Requests for game files, or posts that link to them, are deleted.
- foxhound is not affiliated with or endorsed by the title's developer or publisher, Microsoft or NVIDIA. Names
  and trademarks belong to their owners.

## License

GPL-3.0-or-later, as VIctory. See [LICENSE](LICENSE); the one file that is not covered is named in
[docs/asset-policy.md](docs/asset-policy.md).
