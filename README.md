# HB9IIU Experimental H.266 Player

An experimental receiver window for QO-100 DATV. It shows the live spectrum of the transponder at the
bottom: click a signal to tune it and watch it. It decodes with ffplay, so besides H.265 and H.264 it can
also show **H.266 (VVC)**.

**Tuners:**

- **MiniTiouner:** tested.
- **PicoTuner (USB):** the code supports it, but it is **not tested yet**. Testers are welcome: please tell me if it works.
- **PicoTuner over Ethernet:** not supported.

## Use (Windows 10 / 11)

1. Download the zip from *Releases* and unzip it anywhere.
2. Plug in the tuner and start `HB9IIU-Experimental-H266-Player.exe`. (Same drivers as Open Tuner; close other
   programs that use the tuner first.)
3. Click a signal in the spectrum. The first picture can take 10-15 seconds (the bar says
   *Waiting for keyframe*): that is normal.

Optional: copy `player2.example.json` to `player2.json` to set the LNB offset, LNB power and so on.
The spectrum comes from BATC and needs internet.

## Licence

GPL v3 (it contains code from Open Tuner by ZR6TG and contributors). The source is in `src/`.
`ffplay.exe` in the zip is a free FFmpeg build (LGPL), see `FFMPEG-LICENSE.txt`.

73, HB9IIU
