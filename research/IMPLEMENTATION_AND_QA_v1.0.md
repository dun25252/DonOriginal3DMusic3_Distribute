# Ver.1.0 Genuine-3D prototype — implementation and QA, 2026-10-30

## Status (NOT a formal public release)
Windows compile and synthetic self-test: **PASS**.
Original measured-IoSR BRIR tests: **PASS** for valid file structure, finite samples, and monotonic relative early/late energy.
Genuine music/complete four-stem render and listening evaluation: **NOT YET VERIFIED**.
Do not describe as completed 3D spatial audio or guaranteed high fidelity.

## GitHub reference
Research branch: `research/3d-externalization-v1.0`.
Windows verification: https://github.com/dun25252/DonOriginal3DMusic3_Distribute/actions/runs/41027489390

Changes:
- SpatialDepthEnvelope.cs: direct (<~8ms), early (~8-80ms), late (>~80ms) smooth measured room IR envelope; L/R share the same envelope to preserve time/level difference.
- DistributionAudioEngine.cs: HUTUBS pp3 H3 direct response follows actual azimuth/elevation; IoSR measured F/B/UF/UB/DF + F sideL/sideR room IR. Seven binaural spatial directions, 14 measured WAVs at render time.
- SpatialObjectRouter.cs: 5 selectable vocal seats, spread supporting drums and other accompaniment through measured elevated IRs, retain separate stems and residual.
- Program.cs: 5-seat GUI and saved settings, previous front/rear settings migrated.
- package_v1_0.py and package_v1_0_offline.ps1: additional mandatory UF/UB/DF room WAVs (2 ears each); old base 8 BRIR files remain.

## Original measurements (from authentic v1.48 source archive)
All 5 measured BRIRs: 48 kHz, 65536 frames / ear.
Diagnostic early first 80ms:late energy [dB]:

| Position | Measured | Clarity reference | Ambience reference | Relatively distant reference |
|---|---:|---:|---:|---:|
| F | 28.87 | 32.63 | 29.12 | 26.26 |
| B | 25.91 | 29.51 | 26.21 | 23.66 |
| UF | 28.46 | 32.22 | 28.71 | 25.83 |
| UB | 27.12 | 30.84 | 27.41 | 24.71 |
| DF | 30.31 | 34.14 | 30.56 | 27.61 |

These metrics are NOT formal direct-to-reverberant ratio (DRR) or proof of perceived distance.
HUTUBS measured source radius is 1.47m, so near-field physical distances cannot be claimed.

## Experimental update package
The verified research EXE and six new measured UF/UB/DF room WAVs are combined as an update archive. SHA-256:
`0e25f45193be958ecdba080d13d8593dff431fb6648b18a6215e72ea196de832`
Archive files: executable + 6 measured IRs + readme (8 files).
3×~24MiB downloadable parts were CRC checked and rejoined to the identical SHA-256.
This update needs an EXISTING complete installation with tools/assets/models.

## Explicit pending release gates
1. Run the revised EXE on Windows with existing complete install + elevated BRIR update.
2. Convert at least one complete FLAC with UTF-8 Japanese title and DISCNUMBER metadata; test upper/back/down GUI choices.
3. Probe output 48kHz/24-bit FLAC and compare level-matched original vs old vs prototype for high-frequency loss, onset transient, bass impact, ILD/ITD and IACC.
4. Assess five-direction and distance perception subjectively with the actual listener/headphones; check HRTF mismatch. Static stereo FLAC does not provide head tracking.
5. Only if 1–4 pass, promote to actual private distribution; retain Ver.1.0 as requested.

## Licensing
Original IoSR BRIR copyright/attribution and model/FFmpeg redistribution licenses remain applicable, even for private friend-to-friend distribution.

## Additional measured-audio execution results (2026-10-30)
- Linux FFmpeg was tested with all five simulated stem inputs and all 14 real (original v1.48) measured 48kHz mono ear IRs. Full 19-input binaural afir/amix filter graph returned exit code 0 and produced a valid 48kHz stereo FLAC (24-bit write mode).
- Output: 24,000 frames, 2 channels, finite samples; peak ≈0.00261736, RMS ≈0.00058072. Test is deliberately low level and does not validate final loudness or a song's spectral fidelity.
- Linux FFmpeg HUTUBS pp3 SOFA smoke tests completed at (azimuth,elevation) 0,+30; 180,+30; 0,-30; 90,0; -90,0 with no FFmpeg error. This checks the HRTF option syntax only.
- Authenticated 5-position original IoSR BRIRs were used to render 15 artificial 3-second direction/depth files as a separate listening test archive. These files are test signals, not a genuine song.
- These tests DO NOT cover the Windows EXE processing an actual 4-stem separated music recording with the complete H3+IoSR pipeline, or validated subjectively perceived envelopment.

## 2026-10-30 Bass clarity / vocal envelopment / output name update
- User feedback: overbearing low-frequency thump, voice head-locked to one front point despite clear recording, needs high fidelity.
- `SpatialObjectRouter.Graph`: independent bass-stem highpass at 32Hz plus 95Hz narrow bell -2.7dB to reduce boom, default bass 75% with 30–100% saved GUI adjustment. No global bass EQ on all music.
- `SpatialDepthEnvelope.IsolateLateAmbient`: remove vocal direct impulse through ~18ms after peak, smooth fade of measured L/R room reflections up to 72ms, preserve later measured room tail.
- `DistributionAudioEngine.BuildLateVocalAmbientIr`: renders side-left, side-right and elevated rear measured late BRIRs as six additional mono IRs without duplicating the direct vocal into multiple speaker positions.
- `SpatialObjectRouter.Graph`: highpass at 160Hz/lowpass 9kHz on late vocal ambience only, soft send gains controlled with 0–100% GUI setting (default 55%). The main vocal direct path remains unfiltered and independently placed.
- `Program.cs`: adds two saved WinForms controls, bass and vocal-space percentages. FLAC output names `曲名_v1.0_Original3D.flac` with `曲名(2)_v1.0_Original3D.flac` when duplicates exist.
- Windows GUI compile and structural self-test PASS: https://github.com/dun25252/DonOriginal3DMusic3_Distribute/actions/runs/41032047400
- Independent synthetic FFmpeg test PASS on Linux using 5 audio streams and 20 measured/test IRs: filtergraph had 110 valid named pads, no unconnected pads, stereo FLAC 48000Hz/24bit, 28800 audio frames, peak ~0.00176. **Not** an end-to-end full Windows HTDemucs music test.
- Complete runtime ZIP was created locally using prior 711-file complete distribution ZIP and current Windows-compiled EXE; every entry CRC verified, retained all measured BRIRs/models/tools/licenses, duplicate/title tests covered in executable selftest.
- Complete ZIP name `DonOriginal3DMusic3_v1.0_Original3D_Windows.zip`, bytes 457303724, SHA256 `a0e1b7ad139f0dd322f0d95653ed197a3a3d717b8aa27e95baee2d87b6effe0f`.
- 10 × <=48MiB pieces and a small restore kit rebuilt and SHA256-rejoined successfully.
- Real track and subjective headphone envelopment/fidelity NOT TESTED yet; the earlier public repo Code download still does not include this large ZIP.
