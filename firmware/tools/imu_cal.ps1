<#
  AdaptiveMotionIMU: temperature-drift log and 6-position accelerometer calibration (firmware >= 1.3.0).

    powershell -ExecutionPolicy Bypass -File imu_cal.ps1 -Port COM16 -Diag 20     # log drift for 20 minutes
    powershell -ExecutionPolicy Bypass -File imu_cal.ps1 -Port COM16 -AccCal      # 6-position accel calibration
    powershell -ExecutionPolicy Bypass -File imu_cal.ps1 -Port COM16 -Reset       # remove the accel calibration

  Close SimHub / other serial monitors first.
#>
param([string]$Port = "COM16", [int]$Diag = 0, [switch]$AccCal, [switch]$Reset, [string]$Out = "diag.csv")
$ErrorActionPreference = "Stop"
$inv = [System.Globalization.CultureInfo]::InvariantCulture

function Crc8([byte[]]$b, [int]$from, [int]$count) {
    $c = 0
    for ($i = $from; $i -lt $from + $count; $i++) {
        $c = $c -bxor $b[$i]
        for ($k = 0; $k -lt 8; $k++) { if ($c -band 0x80) { $c = (($c -shl 1) -bxor 0x07) -band 0xFF } else { $c = ($c -shl 1) -band 0xFF } }
    }
    return $c
}
$script:buf = New-Object System.Collections.Generic.List[byte]
$script:tmp = New-Object byte[] 4096
# Read for $ms milliseconds; returns @{ Texts = [...]; Acc = [list of @(ax,ay,az)]; Gyro = [...]; Flags = int }
function Pump([int]$ms) {
    $res = @{ Texts = New-Object System.Collections.ArrayList; Acc = New-Object System.Collections.ArrayList; Gyro = New-Object System.Collections.ArrayList; Flags = 0 }
    $end = (Get-Date).AddMilliseconds($ms)
    while ((Get-Date) -lt $end) {
        $n = 0
        try { $n = $sp.Read($script:tmp, 0, $script:tmp.Length) } catch [System.TimeoutException] { }
        for ($i = 0; $i -lt $n; $i++) { $script:buf.Add($script:tmp[$i]) }
        while ($script:buf.Count -ge 6) {
            if ($script:buf[0] -ne 0xA5 -or $script:buf[1] -ne 0x5A) { $script:buf.RemoveAt(0); continue }
            $len = $script:buf[3]; if ($script:buf.Count -lt 6 + $len) { break }
            $f = $script:buf.GetRange(0, 6 + $len).ToArray()
            if ((Crc8 $f 2 (3 + $len)) -ne $f[5 + $len]) { $script:buf.RemoveAt(0); continue }
            $script:buf.RemoveRange(0, 6 + $len)
            if ($f[2] -eq 0x02) { [void]$res.Texts.Add([System.Text.Encoding]::ASCII.GetString($f, 5, $len)) }
            elseif ($f[2] -eq 0x01 -and $len -eq 30) {
                [void]$res.Acc.Add(@(([BitConverter]::ToInt16($f, 9) / 1000.0), ([BitConverter]::ToInt16($f, 11) / 1000.0), ([BitConverter]::ToInt16($f, 13) / 1000.0)))
                [void]$res.Gyro.Add(@(([BitConverter]::ToInt16($f, 15) / 10.0), ([BitConverter]::ToInt16($f, 17) / 10.0), ([BitConverter]::ToInt16($f, 19) / 10.0)))
                $res.Flags = $res.Flags -bor $f[33]
            }
        }
    }
    return $res
}
function Cmd([string]$c, [int]$waitMs = 400) {
    $sp.Write("$c`n"); $r = Pump $waitMs
    foreach ($t in $r.Texts) { Write-Host "  device: $t" -ForegroundColor Green }
    return $r
}

$sp = New-Object System.IO.Ports.SerialPort $Port, 115200
$sp.DtrEnable = $true; $sp.RtsEnable = $true; $sp.ReadTimeout = 50
try { $sp.Open() } catch { Write-Host "Cannot open ${Port}: $($_.Exception.Message)" -ForegroundColor Red; exit 1 }
Start-Sleep -Milliseconds 300; $sp.DiscardInBuffer()
$info = Cmd "INFO"
if (-not ($info.Texts -match "fw=1\.[3-9]")) { Write-Host "Needs firmware 1.3.0 or newer - flash the new .uf2 first." -ForegroundColor Yellow; $sp.Close(); exit 1 }

if ($Reset) { [void](Cmd "ACCCAL 0"); [void](Cmd "ACC") }

if ($Diag -gt 0) {
    $path = Join-Path (Get-Location) $Out
    $w = New-Object System.IO.StreamWriter($path, $false)
    $w.WriteLine("minute,temp_C,ax_mg,ay_mg,az_mg,gx_mdps,gy_mdps,gz_mdps,gref_mg")
    Write-Host "Logging every 30 s for $Diag min. Leave the box flat and untouched. Ctrl+C stops early (file is kept)." -ForegroundColor Cyan
    Write-Host " min   temp  | raw acc mg          | raw gyro mdps            | |a| ref"
    [void](Cmd "DIAG" 200)       # reset the averages
    $t0 = Get-Date
    try {
        while (((Get-Date) - $t0).TotalMinutes -lt $Diag) {
            [void](Pump 30000)
            $sp.Write("DIAG`n"); $r = Pump 400
            $d = $r.Texts | Where-Object { $_ -like "DIAG*" } | Select-Object -First 1
            if (-not $d) { continue }
            # DIAG T=312 a=-37 5 1172 w=-3879 -1111 5675 r=1172
            $m = [regex]::Match($d, "T=(-?\d+) a=(-?\d+) (-?\d+) (-?\d+) w=(-?\d+) (-?\d+) (-?\d+) r=(\d+)")
            if (-not $m.Success) { continue }
            $v = 1..8 | ForEach-Object { [double]$m.Groups[$_].Value }
            $min = ((Get-Date) - $t0).TotalMinutes
            Write-Host ([string]::Format($inv, "{0,5:F1}  {1,5:F1}C | {2,6} {3,6} {4,6} | {5,7} {6,7} {7,7} | {8}", $min, $v[0] / 10, $v[1], $v[2], $v[3], $v[4], $v[5], $v[6], $v[7]))
            $w.WriteLine([string]::Format($inv, "{0:F2},{1:F1},{2},{3},{4},{5},{6},{7},{8}", $min, $v[0] / 10, $v[1], $v[2], $v[3], $v[4], $v[5], $v[6], $v[7]))
            $w.Flush()
        }
    } finally { $w.Close(); Write-Host "Saved $path" -ForegroundColor Cyan }
}

if ($AccCal) {
    Write-Host ""
    Write-Host "6-position accelerometer calibration" -ForegroundColor Cyan
    Write-Host "Put the box in each position below, hands off, then press Enter. Order does not matter,"
    Write-Host "but each of the 6 faces must point down once. Prop it so it does not rock (within ~5 deg is fine)."
    [void](Cmd "ACCCAL 0")       # measure raw values
    $poses = @("lid UP (lying on its back)", "lid DOWN (on the lid)", "USB end UP", "USB end DOWN",
               "flange side UP", "flange side DOWN")
    $means = @()
    foreach ($p in $poses) {
        do {
            Read-Host "  $p  -> Enter" | Out-Null
            [void](Pump 600); $r = Pump 2500
            if ($r.Acc.Count -lt 200) { Write-Host "  too few frames, again" -ForegroundColor Yellow; $ok = $false; continue }
            $mean = @(0.0, 0.0, 0.0); $sd = 0.0
            foreach ($a in $r.Acc) { for ($i = 0; $i -lt 3; $i++) { $mean[$i] += $a[$i] / $r.Acc.Count } }
            foreach ($a in $r.Acc) { for ($i = 0; $i -lt 3; $i++) { $sd += [Math]::Pow($a[$i] - $mean[$i], 2) / $r.Acc.Count } }
            $sd = [Math]::Sqrt($sd)
            $ok = $sd -lt 0.02
            if (-not $ok) { Write-Host ("  box was moving (noise {0:F3} g) - hold still and press Enter again" -f $sd) -ForegroundColor Yellow }
        } while (-not $ok)
        Write-Host ([string]::Format($inv, "    raw = {0,7:F3} {1,7:F3} {2,7:F3} g", $mean[0], $mean[1], $mean[2]))
        $means += , $mean
    }
    # per axis: the pose where it points up (+) and down (-)
    $pos = @($null, $null, $null); $neg = @($null, $null, $null)
    foreach ($m in $means) {
        $ax = 0; for ($i = 1; $i -lt 3; $i++) { if ([Math]::Abs($m[$i]) -gt [Math]::Abs($m[$ax])) { $ax = $i } }
        if ($m[$ax] -gt 0) { $pos[$ax] = $m[$ax] } else { $neg[$ax] = $m[$ax] }
    }
    if ($pos -contains $null -or $neg -contains $null) {
        Write-Host "Not every axis was seen pointing up AND down - repeat with all 6 faces." -ForegroundColor Red; $sp.Close(); exit 1
    }
    $ofs = @(); $scl = @()
    for ($i = 0; $i -lt 3; $i++) { $ofs += ($pos[$i] + $neg[$i]) / 2; $scl += 2 / ($pos[$i] - $neg[$i]) }
    $cmd = [string]::Format($inv, "ACCCAL {0} {1} {2} {3} {4} {5}",
        [Math]::Round($ofs[0] * 1000), [Math]::Round($ofs[1] * 1000), [Math]::Round($ofs[2] * 1000),
        [Math]::Round($scl[0] * 10000), [Math]::Round($scl[1] * 10000), [Math]::Round($scl[2] * 10000))
    Write-Host ""
    Write-Host ([string]::Format($inv, "offset  = {0,7:F3} {1,7:F3} {2,7:F3} g", $ofs[0], $ofs[1], $ofs[2]))
    Write-Host ([string]::Format($inv, "scale   = {0,7:F4} {1,7:F4} {2,7:F4}", $scl[0], $scl[1], $scl[2]))
    [void](Cmd $cmd); [void](Cmd "ACC")
    $r = Pump 2000; $n = 0.0
    foreach ($a in $r.Acc) { $n += [Math]::Sqrt($a[0] * $a[0] + $a[1] * $a[1] + $a[2] * $a[2]) / $r.Acc.Count }
    Write-Host ([string]::Format($inv, "|a| now = {0:F3} g (should be 1.000 +- 0.02)", $n)) -ForegroundColor Cyan
}
$sp.Close()
Write-Host "Copy everything above into the chat."
