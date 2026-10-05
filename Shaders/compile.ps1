# Recompiles the HLSL pixel shaders in this folder to the .ps bytecode WPF's ShaderEffect loads.
# The compiled .ps files are checked in, so a normal build never needs fxc - only run this after
# editing a .fx file. Requires the Windows SDK (fxc.exe).
$fxc = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin" -Recurse -Filter fxc.exe |
       Where-Object { $_.FullName -match "\\x64\\" } | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $fxc) { throw "fxc.exe not found - install the Windows SDK" }
Get-ChildItem $PSScriptRoot -Filter *.fx | ForEach-Object {
    $out = [IO.Path]::ChangeExtension($_.FullName, ".ps")
    & $fxc.FullName /nologo /T ps_3_0 /E main /O3 /Fo $out $_.FullName
    if ($LASTEXITCODE -ne 0) { throw "fxc failed for $($_.Name)" }
    "compiled $($_.Name) -> $(Split-Path $out -Leaf)"
}
