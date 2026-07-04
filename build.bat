@echo off
rem Build Deskify (Release).
pushd "%~dp0"
dotnet build Deskify.sln -c Release
popd
