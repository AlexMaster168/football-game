@echo off
chcp 65001 >nul
setlocal EnableExtensions

rem ============================================================
rem  Football Dream 2026 — сборка и запуск
rem
rem    build.bat          собрать и запустить
rem    build.bat build    только собрать
rem    build.bat run      только запустить (соберёт, если билда нет)
rem    build.bat test     автотест + обновить скриншоты в docs\screenshots
rem
rem  Путь к Unity можно переопределить: set UNITY_PATH=C:\...\Unity.exe
rem ============================================================

set "ROOT=%~dp0"
if "%ROOT:~-1%"=="\" set "ROOT=%ROOT:~0,-1%"
set "EXE=%ROOT%\Builds\Windows\FootballDream.exe"
set "LOG=%ROOT%\Logs\build.log"
if not defined UNITY_PATH set "UNITY_PATH=C:\Program Files\Unity\Hub\Editor\6000.0.77f1\Editor\Unity.exe"

set "MODE=%~1"
if "%MODE%"=="" set "MODE=all"

if /i "%MODE%"=="all"   goto :all
if /i "%MODE%"=="build" goto :build_only
if /i "%MODE%"=="run"   goto :run_only
if /i "%MODE%"=="test"  goto :test
echo Неизвестный режим: %MODE%
echo Используй: build.bat [build^|run^|test]
exit /b 1

:all
call :build || exit /b 1
goto :run

:build_only
call :build
exit /b %errorlevel%

:run_only
if not exist "%EXE%" (
    echo Билда нет — сначала собираю.
    call :build || exit /b 1
)
goto :run

:test
if not exist "%EXE%" (
    call :build || exit /b 1
)
echo Запускаю автотест (около 1.5 минуты, окно игры откроется само)...
pushd "%ROOT%\Builds\Windows"
del /q shot_*.png autotest.log 2>nul
start "" /wait "%EXE%" -autotest -screen-width 1280 -screen-height 720 -screen-fullscreen 0 -logFile player.log
popd
if not exist "%ROOT%\Builds\Windows\autotest.log" (
    echo [ОШИБКА] Автотест не дописал лог. Смотри Builds\Windows\player.log
    exit /b 1
)
type "%ROOT%\Builds\Windows\autotest.log"
call :copy_shots
echo.
echo Скриншоты обновлены: docs\screenshots
exit /b 0

rem ------------------------------------------------------------
:build
if not exist "%UNITY_PATH%" (
    echo [ОШИБКА] Не найден Unity: "%UNITY_PATH%"
    echo Поставь Unity 6000.0.77f1 через Unity Hub или задай переменную UNITY_PATH.
    exit /b 1
)
echo Собираю проект... ^(1-3 минуты; редактор Unity с этим проектом должен быть закрыт^)
if not exist "%ROOT%\Logs" mkdir "%ROOT%\Logs"
"%UNITY_PATH%" -batchmode -quit -projectPath "%ROOT%" -executeMethod FD.EditorTools.ProjectTools.BuildWindows -logFile "%LOG%"
if errorlevel 1 (
    echo [ОШИБКА] Сборка упала. Ошибки компиляции:
    findstr /c:"error CS" "%LOG%"
    echo Полный лог: %LOG%
    exit /b 1
)
if not exist "%EXE%" (
    echo [ОШИБКА] Unity отработал, но exe не появился. Лог: %LOG%
    exit /b 1
)
echo Готово: %EXE%
exit /b 0

rem ------------------------------------------------------------
:run
echo Запускаю игру...
start "" "%EXE%"
exit /b 0

rem ------------------------------------------------------------
:copy_shots
set "SRC=%ROOT%\Builds\Windows"
set "DST=%ROOT%\docs\screenshots"
if not exist "%DST%" mkdir "%DST%"
call :cp shot_menu.png           menu.png
call :cp shot_dream.png          my-team.png
call :cp shot_transfers_real.png transfers.png
call :cp shot_training.png       training.png
call :cp shot_staff.png          staff.png
call :cp shot_wchub.png          world-cup.png
call :cp shot_intro.png          match-intro.png
call :cp shot_match2.png         match.png
call :cp shot_goal.png           goal.png
exit /b 0

:cp
if exist "%SRC%\%~1" copy /y "%SRC%\%~1" "%DST%\%~2" >nul
exit /b 0
