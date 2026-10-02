$dir = "C:\Users\RYO\AppData\Roaming\XIVLauncher\installedPlugins\HDM\1.0.3.0"
[System.AppDomain]::CurrentDomain.add_AssemblyResolve({
    param($s, $e)
    $n = ($e.Name -split ',')[0]
    $p = "$dir\$n.dll"
    if (Test-Path $p) { [System.Reflection.Assembly]::LoadFrom($p) } else { $null }
})
$bytes = [System.IO.File]::ReadAllBytes("$dir\HDM.dll")
$asm = [System.Reflection.Assembly]::Load($bytes)
foreach ($t in $asm.GetTypes()) {
    foreach ($m in $t.GetMethods([System.Reflection.BindingFlags]'Public,NonPublic,Instance,Static')) {
        if ($m.Name -match "Despawn") {
            Write-Output "Method: $($t.FullName).$($m.Name)"
            $body = $m.GetMethodBody()
            if ($body) {
                Write-Output "  IL Length: $($body.GetILAsByteArray().Length)"
            }
        }
    }
}
