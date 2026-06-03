@echo off
echo Building Rust native library...
cd /d "%~dp0rust-core"
cargo build %*
if %ERRORLEVEL% neq 0 (
    echo Rust build failed!
    exit /b %ERRORLEVEL%
)

cd /d "%~dp0"
echo.
echo Building C# project...
dotnet build AquaStorage
if %ERRORLEVEL% neq 0 (
    echo C# build failed!
    exit /b %ERRORLEVEL%
)

echo.
echo Build complete.
echo Run: dotnet run --project AquaStorage
