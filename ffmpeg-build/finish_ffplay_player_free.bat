@echo off
rem ===========================================================================
rem  finish_ffplay_player_free.bat - STEP 2 of 2: collect the free ffplay
rem
rem  Run this AFTER the suite window of build_ffplay_player_free.bat says it is finished.
rem  Put this file next to media-autobuild_suite.bat (C:\mabs).
rem    1. copies ffplay.exe / ffmpeg.exe / ffprobe.exe from local64\bin-video to ffplay-free-output
rem    2. checks that the build really is free (no non-free, GPL, fdk-aac, x264, x265 parts)
rem    3. writes the licence note that goes with it
rem    4. puts the H.266 encoder build (h266-encoder-build) back into bin-video
rem ===========================================================================

setlocal
cd /d "%~dp0"

if not exist local64\bin-video\ffplay.exe (
    echo local64\bin-video\ffplay.exe not found: the build has not finished, or it failed.
    pause
    exit /b 1
)

if not exist ffplay-free-output mkdir ffplay-free-output
copy /y local64\bin-video\ffplay.exe  ffplay-free-output >nul
copy /y local64\bin-video\ffmpeg.exe  ffplay-free-output >nul
copy /y local64\bin-video\ffprobe.exe ffplay-free-output >nul

echo.
echo Checking the build in ffplay-free-output ...
ffplay-free-output\ffplay.exe -hide_banner -buildconf 2>&1 | findstr /i "nonfree enable-gpl fdk libx264 libx265" >nul
if not errorlevel 1 (
    echo.
    echo *** NOT FREE: the build contains non-free or GPL parts. Do NOT share ffplay-free-output. ***
    echo *** Was the build finished, with build_ffplay_player_free.bat?                           ***
    pause
    exit /b 1
)
ffplay-free-output\ffplay.exe -hide_banner -version | findstr /b /c:"ffplay version"
ffplay-free-output\ffmpeg.exe -hide_banner -decoders 2>&1 | findstr /i /c:" vvc " >nul && (echo H.266 decoder: present) || (echo WARNING: no H.266 decoder in this build)
echo Free build: OK (no non-free, GPL, fdk-aac, x264 or x265 in the configuration).

(
echo FFmpeg / ffplay in this package
echo ----------------------------
echo A build of FFmpeg ^(https://ffmpeg.org^), licensed under the GNU Lesser General Public License,
echo version 2.1 or later ^(the licence text that "ffplay -L" prints; full text:
echo https://www.gnu.org/licenses/old-licenses/lgpl-2.1.html^).
echo Configured without any GPL or non-free parts ^(no fdk-aac, no x264, no x265^).
echo Source code: https://code.ffmpeg.org/FFmpeg/FFmpeg ^(the commit is in the version line of ffplay -version^),
echo built with media-autobuild_suite ^(https://github.com/m-ab-s/media-autobuild_suite^).
echo Configure options: --disable-autodetect --disable-debug --disable-doc --enable-sdl2 --enable-d3d11va
echo --enable-dxva2 --enable-schannel
echo The build script is ffmpeg-build/build_ffplay_player_free.bat in the player's repository.
) > ffplay-free-output\FFMPEG-LICENSE.txt

rem --- Put the H.266 encoder build back where QOCAST's H.266 button looks first ---
if exist h266-encoder-build\ffmpeg.exe (
    copy /y h266-encoder-build\ffmpeg.exe  local64\bin-video >nul
    copy /y h266-encoder-build\ffplay.exe  local64\bin-video >nul
    copy /y h266-encoder-build\ffprobe.exe local64\bin-video >nul
    echo The H.266 encoder build is back in local64\bin-video.
)

echo.
echo Done. The free ffplay is in %CD%\ffplay-free-output
pause
endlocal
exit /b 0
