# DonOriginal3DMusic3 Ver.1.0 — Genuine 3D / high-fidelity reconstruction

Status: RESEARCH AND MEASUREMENT. **NOT a completed renderer or public-release EXE.**
Keep the working Ver.1.0 branch unchanged while designing and testing a replacement.

## Root-cause evidence (checked against the original 513,598,015-byte v1.48 ZIP)
- The present SpatialObjectRouter puts sources at fixed horizontal front/back/side directions, with no object distance or elevation parameter. More directional channels is not 3D reconstruction.
- Original IoSR BRIR_MANIFEST.json defines five actual measured positions: F=(0°,0°), B=(180°,0°), UF=(0°,+30°), UB=(180°,+30°), DF=(0°,-30°). Existing distributed package omits UF/UB/DF even though original v1.48 includes them.
- Original HUTUBS pp3.sofa: 440 HRIR source positions, 44,100 Hz, source radius 1.47 m, measured angles include 0°, +30°, -30° elevation. A 1.47 m dataset is not near-field data for a 0.3 m source.
- IoSR data is one fixed room and measurement head, not a personalized 3D BRIR at arbitrary source locations.
- Current late-room setting 45% attenuates measured late reflections; a lower tail level can reduce envelopment. Raising reverb alone is not a solution.
- Current pipeline risks stage-wise stereo reduction and separated-stem artifacts; final RMS normalization may hide audible dynamic differences. The 4Stem model is not automatically four separately perceived instruments or singers.

## New renderer acceptance criteria
1. Source object metadata **azimuth + elevation + distance + spread + level**, not directions alone; restore original UF/UB/DF impulse measurements and package them.
2. HRTF direction/elevation response in both ears (ITD/ILD + pinna spectrum), HRTF listener selection and calibration rather than one universal pp3.
3. Split direct sound, directional early reflections, spatially diffuse late reverberation; preserve actual head-related binaural differences and manage DRR coherently for depth.
4. Do not claim accurate <1 m near-field distance from far-field-only measurements. Either add legitimate near-field HRTFs or label proximity approximation.
5. Protect high-frequency clarity, transients, phase, midrange vocals and low-end impact, using confidence-aware source separation or fallback where stems bleed.
6. End-to-end measured A/B tests at equal loudness, known directions (front/back/up/down/side), near/far, original music and user listening. Check waveform/tag and GUI invariants; compile-only selftests never certify perceptual 3D.
7. Head tracking cannot be represented by a static FLAC alone; do not promise 3DoF head-locked cancellation without dynamic playback.

## Measured impulse-response checks from v1.48 source (internal analysis)
All positions are 48 kHz × 65536 mono samples per ear. Using direct peak plus next 80 ms vs later wave-energy:
F +28.87 dB, B +25.90 dB, UF +28.46 dB, UB +27.13 dB, DF +30.31 dB.
These are test-specific descriptors, not standardized DRR or proof of spatial realism.
The fixed positions have distinct IACC (F .827, B .629, UF .813, UB .784, DF .699).
Test source and machine-readable results are available in the separate internal measurement package.

## Build and release policy
The Ver.1.0 EXE currently built on the prior branch remains an insufficient 3D prototype.
Do not release another "fixed 3D" EXE until actual audio render, quantitative measurement,
and listening demonstrate both convincing envelopment and fidelity improvement.
Do not launch paid GitHub Actions for analysis-only commits.
