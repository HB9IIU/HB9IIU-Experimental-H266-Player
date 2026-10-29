# HB9IIU Experimental H.266 Player

An experimental QO-100 DATV receiver window. It works like QOCAST Player 2 (spectrum at the bottom, click a
signal to tune it), but it decodes with ffplay, so it can show **H.266 (VVC)** as well as H.265 and H.264.
The title bar says what is playing, for example `Currently playing H.265 1280x720`.

**Tested with:** a MiniTiouner.
**PicoTuner:** the code supports it, but it is **not tested yet**. Testers are welcome: please tell me if it works.

## Use (Windows 10 / 11)

1. Download the zip from *Releases* and unzip it anywhere.
2. Plug in the tuner and start `qocast-player_2_h266test.exe`. (Same drivers as Open Tuner; close other
   programs that use the tuner first.)
3. Click a signal in the spectrum. The first picture can take 10-15 seconds (the bar says
   *Waiting for keyframe*): that is normal.

Optional: copy `player2.example.json` to `player2.json` to set the LNB offset, LNB power and so on.
The spectrum comes from BATC and needs internet.

## Licence

GPL v3 (it contains code from Open Tuner by ZR6TG and contributors). The source is in `src/`.
`ffplay.exe` in the zip is a free FFmpeg build (LGPL v3), see `FFMPEG-LICENSE.txt`.

73, HB9IIU
