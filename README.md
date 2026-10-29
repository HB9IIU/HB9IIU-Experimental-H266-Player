# HB9IIU Experimental H.266 Player

## [Download the latest version (Windows zip)](https://github.com/HB9IIU/HB9IIU-Experimental-H266-Player/releases/latest)

An experimental receiver window for QO-100 DATV. It shows the live spectrum of the transponder at the
bottom: click a signal to tune it and watch it. It decodes with ffplay, so besides H.265 and H.264 it can
also show **H.266 (VVC)**.

**Tuners:**

- **MiniTiouner:** tested.
- **PicoTuner (USB):** tested, works (received an H.266 test transmission).
- **PicoTuner over Ethernet:** not supported.

## Use (Windows 10 / 11)

1. [Download the zip](https://github.com/HB9IIU/HB9IIU-Experimental-H266-Player/releases/latest) (under *Assets*) and unzip it anywhere.
2. Plug in the tuner and start `HB9IIU-Experimental-H266-Player.exe`. (Same drivers as Open Tuner; close other
   programs that use the tuner first.)
3. Click a signal in the spectrum. The first picture can take 10-15 seconds (the bar says
   *Waiting for keyframe*): that is normal.

The spectrum comes from BATC and needs internet.

## Right-click menu

- **Spectrum:** show or hide the spectrum (a double-click on the picture does the same).
- **Always on top** and **Title bar:** how the window behaves.
- **LNB power:** *Off*, *Vertical* or *Horizontal*. Choose one if your tuner powers the LNB; the default is *Off*.
  The signal is tuned again at once, and the choice is remembered.
- **Quit**

## Licence

GPL v3 (it contains code from Open Tuner by ZR6TG and contributors). The source is in `src/`.
`ffplay.exe` in the zip is a free FFmpeg build (LGPL), see `FFMPEG-LICENSE.txt`. The exact FFmpeg source of that
build is attached to the release as `ffmpeg-c0f6ae570-source.zip`.

73, HB9IIU
