<#
  AdaptiveMotionIMU bench check (Windows PowerShell 5+ / pwsh, no installs needed).

  Right-click > "Run with PowerShell", or:
    powershell -ExecutionPolicy Bypass -File imu_check.ps1 -Port COM8
    powershell -ExecutionPolicy Bypass -File imu_check.ps1 -Port COM8 -Live 30   # live tilt view for 30 s
    powershell -ExecutionPolicy Bypass -File imu_check.ps1 -Port COM8 -Live 60 -Csv tilt.csv   # + log every frame

  Close the Arduino IDE serial monitor and SimHub first: only one program can open the COM port.
#>
param([string]$Port = "COM8", [int]$Seconds = 4, [int]$Live = 0, [string]$Csv = "")

$ErrorActionPreference = "Stop"
function Crc8([byte[]]$b, [int]$from, [int]$count) {
    $c = 0
    for ($i = $from; $i -lt $from + $count; $i++) {
        $c = $c -bxor $b[$i]
        for ($k = 0; $k -lt 8; $k++) { if ($c -band 0x80) { $c = (($c -shl 1) -bxor 0x07) -band 0xFF } else { $c = ($c -shl 1) -band 0xFF } }
    }
    return $c
}
$flagNames = @{0x01="I2C-ERROR"; 0x02="gyro-calibrated"; 0x04="ACCEL-SATURATED"; 0x08="GYRO-SATURATED";
               0x10="clone-die(WHO_AM_I!=0x68)"; 0x20="frames-dropped"; 0x40="tilt-correction-gated"; 0x80="still"}

Write-Host "Opening $Port ..." -ForegroundColor Cyan
$sp = New-Object System.IO.Ports.SerialPort $Port, 115200, ([System.IO.Ports.Parity]::None), 8, ([System.IO.Ports.StopBits]::One)
$sp.DtrEnable = $true; $sp.RtsEnable = $true; $sp.ReadTimeout = 50
try { $sp.Open() } catch { Write-Host "Cannot open ${Port}: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Is another program (Arduino IDE serial monitor, SimHub) using it? Is it the right COM number?"; exit 1 }
Start-Sleep -Milliseconds 800     # fw < 1.2.1 only notices an open port every 0.5 s
$sp.DiscardInBuffer()
$sp.Write("INFO`n"); Start-Sleep -Milliseconds 100; $sp.Write("BIAS`n")

$log = $null
if ($Csv) {
    $log = New-Object System.IO.StreamWriter((Join-Path (Get-Location) $Csv), $false)
    $log.WriteLine("t_us,ax_g,ay_g,az_g,gx_dps,gy_dps,gz_dps,q0,q1,q2,q3,peak_mg,vib_mg,flags,dropped,text")
}
$inv = [System.Globalization.CultureInfo]::InvariantCulture
$buf = New-Object System.Collections.Generic.List[byte]
$texts = @(); $imu = 0; $bad = 0; $raw = 0; $last = $null; $flagsSeen = 0
$tEnd = (Get-Date).AddSeconds([Math]::Max($Seconds, $Live)); $nextLive = Get-Date; $nextPrint = (Get-Date).AddSeconds(2)
$tmp = New-Object byte[] 4096
while ((Get-Date) -lt $tEnd) {
    $n = 0
    try { $n = $sp.Read($tmp, 0, $tmp.Length) } catch [System.TimeoutException] { }
    if ($n -gt 0) { $raw += $n; for ($i = 0; $i -lt $n; $i++) { $buf.Add($tmp[$i]) } }
    # parse frames
    while ($buf.Count -ge 6) {
        if ($buf[0] -ne 0xA5 -or $buf[1] -ne 0x5A) { $buf.RemoveAt(0); continue }
        $len = $buf[3]; if ($buf.Count -lt 6 + $len) { break }
        $f = $buf.GetRange(0, 6 + $len).ToArray()
        if ((Crc8 $f 2 (3 + $len)) -ne $f[5 + $len]) { $bad++; $buf.RemoveAt(0); continue }
        $buf.RemoveRange(0, 6 + $len)
        if ($f[2] -eq 0x02) { $t = [System.Text.Encoding]::ASCII.GetString($f, 5, $len); $texts += $t; Write-Host "  device: $t" -ForegroundColor Green
            if ($log) { $log.WriteLine(",,,,,,,,,,,,,,,""$t""") } }
        elseif ($f[2] -eq 0x01 -and $len -eq 30) {
            $imu++; $p = 5
            $last = [pscustomobject]@{
                ax = [BitConverter]::ToInt16($f, $p + 4) / 1000.0; ay = [BitConverter]::ToInt16($f, $p + 6) / 1000.0; az = [BitConverter]::ToInt16($f, $p + 8) / 1000.0
                gx = [BitConverter]::ToInt16($f, $p + 10) / 10.0; gy = [BitConverter]::ToInt16($f, $p + 12) / 10.0; gz = [BitConverter]::ToInt16($f, $p + 14) / 10.0
                q0 = [BitConverter]::ToInt16($f, $p + 16) / 16384.0; q1 = [BitConverter]::ToInt16($f, $p + 18) / 16384.0
                q2 = [BitConverter]::ToInt16($f, $p + 20) / 16384.0; q3 = [BitConverter]::ToInt16($f, $p + 22) / 16384.0
                peak = [BitConverter]::ToUInt16($f, $p + 24); vib = [BitConverter]::ToUInt16($f, $p + 26); flags = $f[$p + 28]; drop = $f[$p + 29] }
            $flagsSeen = $flagsSeen -bor $last.flags
            if ($log) {
                $log.WriteLine([string]::Format($inv, "{0},{1:F3},{2:F3},{3:F3},{4:F1},{5:F1},{6:F1},{7:F4},{8:F4},{9:F4},{10:F4},{11},{12},{13},{14},",
                    [BitConverter]::ToUInt32($f, $p), $last.ax, $last.ay, $last.az, $last.gx, $last.gy, $last.gz,
                    $last.q0, $last.q1, $last.q2, $last.q3, $last.peak, $last.vib, $last.flags, $last.drop))
            }
        }
    }
    if ($Live -gt 0 -and $last -and (Get-Date) -ge $nextLive) {
        $nextLive = (Get-Date).AddMilliseconds(200)
        $sinp = 2 * ($last.q0 * $last.q2 - $last.q3 * $last.q1); $sinp = [Math]::Max(-1, [Math]::Min(1, $sinp))
        $tiltA = [Math]::Asin($sinp) * 180 / [Math]::PI
        $tiltB = [Math]::Atan2(2 * ($last.q0 * $last.q1 + $last.q2 * $last.q3), 1 - 2 * ($last.q1 * $last.q1 + $last.q2 * $last.q2)) * 180 / [Math]::PI
        $qn = [Math]::Sqrt($last.q0*$last.q0 + $last.q1*$last.q1 + $last.q2*$last.q2 + $last.q3*$last.q3)
        $line = "|q| {9:F3} | acc[g] {0,6:F3} {1,6:F3} {2,6:F3} | gyro[dps] {3,6:F1} {4,6:F1} {5,6:F1} | tilt A {6,6:F1}  B {7,6:F1} deg | vib {8} mg" -f `
            $last.ax, $last.ay, $last.az, $last.gx, $last.gy, $last.gz, $tiltA, $tiltB, $last.vib, $qn
        Write-Host "`r$line   " -NoNewline
        if ((Get-Date) -ge $nextPrint) { $nextPrint = (Get-Date).AddSeconds(2); Write-Host "" }   # keep a line every 2 s
    }
}
$sp.Close()
if ($log) { $log.Close(); Write-Host ""; Write-Host "Logged every frame to $(Join-Path (Get-Location) $Csv)" -ForegroundColor Cyan }
if ($Live -gt 0) { Write-Host "" }

Write-Host ""
Write-Host "=== Result ($([Math]::Max($Seconds, $Live)) s) ===" -ForegroundColor Cyan
Write-Host ("bytes received: {0}   IMU frames: {1} (~{2:F0}/s)   CRC errors: {3}" -f $raw, $imu, ($imu / [Math]::Max($Seconds, $Live)), $bad)
if ($raw -eq 0) {
    Write-Host "Nothing received. Either the firmware is not flashed (double-tap RST->GND, copy the .uf2 onto the NICENANO drive)," -ForegroundColor Yellow
    Write-Host "or this COM port is the bootloader / another device. Check Device Manager > Ports." -ForegroundColor Yellow
} elseif ($imu -eq 0) {
    Write-Host "Data but no valid frames: wrong device on this port, or old firmware." -ForegroundColor Yellow
}
if ($last) {
    $mag = [Math]::Sqrt($last.ax * $last.ax + $last.ay * $last.ay + $last.az * $last.az)
    Write-Host ("last sample: acc = {0:F3} {1:F3} {2:F3} g  |a| = {3:F3} g   gyro = {4:F1} {5:F1} {6:F1} deg/s" -f $last.ax, $last.ay, $last.az, $mag, $last.gx, $last.gy, $last.gz)
    $names = @(); foreach ($k in $flagNames.Keys) { if ($flagsSeen -band $k) { $names += $flagNames[$k] } }
    Write-Host ("flags seen: 0x{0:X2} {1}" -f $flagsSeen, ($names -join ", "))
    if ([Math]::Abs($mag - 1) -gt 0.08) { Write-Host "WARNING: |a| should be ~1.00 g at rest." -ForegroundColor Yellow } else { Write-Host "|a| at rest OK" -ForegroundColor Green }
}
Write-Host ""
Write-Host "Copy everything above into the chat." -ForegroundColor Cyan
if ($Host.Name -eq "ConsoleHost" -and -not $env:WT_SESSION) { Read-Host "Press Enter to close" | Out-Null }
