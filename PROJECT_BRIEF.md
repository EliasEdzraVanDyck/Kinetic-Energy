# GD3 Retake Kinetic Energy — Project Brief

A briefing for brainstorming. Paste this into a chat to give it full context of the game.

## The game in one paragraph

A 3D physics platformer (Unity 6, URP) about **launching yourself**. You play a small sphere that can't jump — instead you charge up ballistic launches, fly along a predicted arc, and crash into things. Crashing is good: it refunds energy, sticks you to surfaces, and chains into the next launch. The core loop is aim → commit → fly → crash → relaunch, with an energy economy deciding how far each launch can go and how much you get back. The feel targets are speed, commitment, and mid-flight decision-making in slow motion.

## Core mechanics

**Energy tank (0–100%)**: every launch spends energy proportional to its charge; crashes refund some. You start levels at 20%. A minimum reserve prevents stranding yourself on the ground (midair launches may spend everything as a save-throw).

**Grounded aim**: hold right mouse / left trigger — the mouse steers a yellow aim arrow, charge builds over time (accelerating ramp), a dotted trajectory line + landing crosshair (simulated with the real physics engine, always accurate) shows exactly where you'll land. Fire with left mouse / right trigger.

**Up-charge**: hold Space/South — charges an accelerating straight-up launch, released to fire. Charge fills in real time through the slow-mo.

**Midair aim (the signature mechanic)**: right mouse / left trigger midair opens a first-person aim: time slows to 20%, you're frozen in place, you look where to go, and dial the energy in/out with the mouse wheel / right stick (with an accelerating dial). The trajectory line and reticle show the shot. Fire to launch; release without firing to resume your original flight path. Camera zooms in as you dial more energy.

**Ground pound**: hold E/West midair — charges (accelerating) then slams straight down. Landing a pound BOUNCES you: a free hop plus a 0.5 s slow-mo window. The pound's flight cost is refunded as a wash immediately; opening an aim inside the window claims a **boost** (1.5× the pound's spend) and starts that aim instantly charged with ALL current energy, gravity off. Fired pound-window launches count as midair launches (better refund formula). Letting the window lapse forfeits the boost.

**Crashing**: any launch ending on a surface registers a crash: you stop dead, stick, and get an energy refund. Green STICKY surfaces (opt-in `StickySurface` component) hold you until you launch; everything else drops you after 0.3 s (flat ground releases you to walking immediately). Crashing into a floating wall mid-course grants a limited window of relaunches — miss it and you fall.

**Refund rules**: grounded launch → spend × grounded multiplier; midair launch → spend × (base + 0.3 × spend) — big commitments pay back MORE than they cost; pound → full wash + claimable 1.5× boost.

**Game speed**: launches speed the game up (base 200%, +1% per 1% energy spent), with a descent ramp adding up to +50% as you fall toward the predicted landing. Charging/aiming slows it to 20%. Vertical charges keep the camera at full speed. All non-player moving objects (platforms, enemies) run on a hybrid clock: they slow with bullet-time but do NOT speed up with launches.

**Walking is being phased out**: WASD/stick movement and air-nudging are disabled by default (M toggles them for testing). The game is heading toward launches being the ONLY locomotion.

## Levels / scenes

- **QuarryNew** ("the Sandbox"): open walled arena, platforms and perches along the walls, 8 floating target spheres (crash to collect, they respawn randomly, min 5 active, min height enforced). Free-play tuning ground. Currently the only scene in builds (desktop + WebGL).
- **QuarryNoDamping**: A/B copy where launches have zero damping but per-charge forces are solved at startup to land at identical distances — testing whether drag matters to feel.
- **Level1**: platform run with growing gaps (each jump needs more energy), then floating walls to chain wall-crashes across, red DamageWalls that respawn you. Tests gradual energy drain over flight and the wall-crash relaunch limit.
- **Level2**: moving platforms (ping-pong paths). While aiming midair, blue lead-arrows show where each platform will be when you'd land, scaled by your predicted flight time. Platforms carry the player.
- **Level3**: wandering enemies (radius or full-platform-surface modes, edge margin). They attack like the player moves: detect a grounded player in range, wind up 0.5 s (orange flash), then ballistically launch at your OLD position (no homing — the telegraph is the counterplay). A hit knocks you back, drains 15% energy, and locks launching/aiming for 0.5 s (energy bar flashes red and pulses). Launching into an enemy kills it; player respawn revives everything.
- **Gauntlet**: five-beat linear course comparing two slowdown-resource designs (separate 2 s aim budget refilled by crashes vs. draining the main tank), with run logging.
- Every scene shares a pause menu (controls text, feedback-form button, restart) and the HUD: yellow energy bar + blue charge preview + orange pound-boost preview, all prefabs.

## Current tuning (feel numbers)

Launch force 60–130 (charge-scaled), max charge 1.5 s, gravity −30, damping 2.8→1.0 by charge (drag shapes arcs; a no-damping variant is being A/B tested), pound damping 0.2, charge slow-mo 0.2×, flight speed-up 2×+, camera trails launches (0.25 s smoothing, 0.18 s for vertical, eased recovery ~0.5 s). A full-charge 45° launch flies ≈107 m and ≈79 m high.

## Design questions currently open (brainstorm fodder)

- Walking removal: what does a launch-only game need so grounded moments don't feel stuck?
- Damping vs no damping: does drag-shaped flight (fast start, drooping tail) beat clean parabolas at identical distances?
- Enemy roster: currently one launcher-type enemy; what other enemies suit a game where the player IS a projectile?
- Low-angle launches still end early (arc grazes the ground long before the crosshair) — needs a design answer more than a physics hack; one attempted fix (skim-through to the landing point) broke game feel and was reverted.
- The pound loop (pound → boosted aim → huge launch) is strong: should it be the intended movement tech ceiling or does it need a cost?
- What is the win condition / goal structure? Current levels are tests; targets (Quarry) and finish lines (Level1/Gauntlet) exist, but the "real game" shape is undecided.
- Slowdown resource: unlimited vs aim-budget vs energy-drain (the Gauntlet A/B) — unresolved.

## Constraints worth knowing

Exam project (GD3 retake), solo student developer, Unity 6000.4.8f1, all geometry is primitive blocks/spheres with flat colors, no audio yet, playtesting is the main evaluation tool (a feedback form is wired into every scene's menu). Desktop and WebGL builds of QuarryNew exist.
