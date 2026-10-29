@echo off
rem ===========================================================================
rem  build_ffplay_player_free.bat - STEP 1 of 2: a FREE (LGPL) ffplay for the H.266 test player
rem
rem  The ffplay of build_datv_ffmpeg_x265_H266.bat is NOT allowed to be given to others (it contains
rem  fdk-aac, x264 and x265 with --enable-nonfree). This script builds one that IS:
rem    - license LGPL, no fdk-aac, no x264/x265, no GPL parts, no non-free parts
rem    - SDL2 (so ffplay.exe is built), D3D11 / DXVA2 hardware decoding
rem    - H.264, H.265 and H.266 are decoded by FFmpeg's own decoders (no extra library)
rem  It is meant for decoding only (the test player); it is NOT the FFmpeg QOCAST transmits with.
rem
rem  Put this file NEXT TO media-autobuild_suite.bat (C:\mabs) and run it. The suite then builds in its
rem  own window; when that window says it is finished, run STEP 2: finish_ffplay_player_free.bat
rem  (it collects the result and puts the H.266 encoder build back).
rem
rem  Before it starts, the current C:\mabs\local64\bin-video (with the H.266 encoder) is copied to
rem  C:\mabs\h266-encoder-build (only once, never overwritten): the suite writes its result to
rem  bin-video, over what is there.
rem ===========================================================================

setlocal
cd /d "%~dp0"

if not exist media-autobuild_suite.bat (
    echo media-autobuild_suite.bat not found next to this file.
    echo Put build_ffplay_player_free.bat in the suite folder, e.g. C:\mabs
    pause
    exit /b 1
)

if not exist build mkdir build

rem --- Keep the current build (with the H.266 encoder), once ----------------------
if exist local64\bin-video\ffmpeg.exe (
    if not exist h266-encoder-build (
        echo Keeping the current ffmpeg/ffplay/ffprobe in h266-encoder-build ...
        mkdir h266-encoder-build
        copy /y local64\bin-video\ffmpeg.exe  h266-encoder-build >nul
        copy /y local64\bin-video\ffplay.exe  h266-encoder-build >nul
        copy /y local64\bin-video\ffprobe.exe h266-encoder-build >nul
    )
)

rem --- Use half the logical processors for compiling (at least 1) -----------
set /a CORES=%NUMBER_OF_PROCESSORS%/2
if %CORES% LSS 1 set CORES=1

rem --- FFmpeg source: latest development version (H.266 decoding needs a recent one) ---
set FFMPEG_PATH=https://code.ffmpeg.org/FFmpeg/FFmpeg.git

rem --- Answers to all suite questions (1 = Yes, 2 = No unless noted) -------
rem     license2=4 is LGPL. fdkaac=2, x2643=2, x2653=2: nothing that is not free.
(
echo [compiler list]
echo license2=4
echo standalone=2
echo av1an=2
echo vpx2=2
echo aom=2
echo rav1e=2
echo dav1d=2
echo libavif=2
echo libheif=2
echo jpegxl=2
echo x2643=2
echo x2653=2
echo other265=2
echo svthevc=2
echo xvc=2
echo uvg266=2
echo vvenc=2
echo vvdec=2
echo svtav1=2
echo svtvp9=2
echo flac=2
echo fdkaac=2
echo faac=2
echo exhale=2
echo mediainfo=2
echo soxB=2
echo ffmpegB2=1
echo ffmpegUpdate=1
echo ffmpegChoice=1
echo ffmpegKeepLegacyOpts=2
echo mp4box=2
echo rtmpdump=2
echo mpv=2
echo vlc=2
echo bmx=2
echo curl=2
echo cyanrip2=2
echo ripgrep=2
echo jq=2
echo jo=2
echo dssim=2
echo gifski=3
echo avs2=2
echo dovitool=2
echo hdr10plustool=2
echo zlib=6
echo CC=2
echo cores=%CORES%
echo deleteSource=1
echo strip=1
echo pack=2
echo logging=1
echo updateSuite=2
echo timeStamp=2
echo ccache=2
echo noMintty=2
echo ffmpegPath=%FFMPEG_PATH%
echo pkgUpdateTime=86400
) > build\media-autobuild_suite.ini

rem --- FFmpeg configure options: decoding and ffplay only ---------------------
(
echo # QOCAST H.266 test player - FREE FFmpeg build, LGPL, for ffplay
echo # Lines starting with # are ignored
echo.
echo --disable-autodetect
echo --disable-debug
echo --disable-doc
echo --enable-sdl2
echo --enable-d3d11va
echo --enable-dxva2
) > build\ffmpeg_options.txt

rem --- Safety check: nothing that is not free may be in the option list ------
findstr /i /c:"nonfree" /c:"enable-gpl" /c:"fdk" /c:"x264" /c:"x265" /c:"vvenc" build\ffmpeg_options.txt >nul && goto :optfail
findstr /x /c:"--enable-sdl2" build\ffmpeg_options.txt >nul || goto :optfail

rem --- Empty mpv options file, so the suite does not stop to ask about it ---
if not exist build\mpv_options.txt (
    echo # mpv is not built for QOCAST> build\mpv_options.txt
)

echo.
echo Free ffplay build: answers and FFmpeg options written to %CD%\build
echo Starting media-autobuild_suite ... when its window says it is finished,
echo run finish_ffplay_player_free.bat (STEP 2).
echo.
call media-autobuild_suite.bat
endlocal
exit /b 0

:optfail
echo.
echo ERROR: build\ffmpeg_options.txt does not contain the free (LGPL) option list.
echo The suite was NOT started. Please report this.
pause
endlocal
exit /b 1
