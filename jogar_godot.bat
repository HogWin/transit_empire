@echo off
chcp 65001 >nul
set "DOTNET_ROOT=D:\Felipe\dotnet"
set "NUGET_PACKAGES=D:\Felipe\nuget_packages"
set "PATH=D:\Felipe\dotnet;D:\Felipe\Godot\Godot_v4.7.2-stable_mono_win64;%PATH%"

echo =========================================================
echo   TRANSIT EMPIRE - Iniciando Jogo 3D no Godot 4.7.2
echo =========================================================
"D:\Felipe\dotnet\dotnet.exe" build "D:\Felipe\transit_empire\src\TransitEmpire.Client\TransitEmpire.Client.sln" --no-restore --nologo -v:q
start "" "D:\Felipe\Godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe" --path "D:\Felipe\transit_empire\src\TransitEmpire.Client"

