param([Parameter(Mandatory)] [string] $ExePath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type -Namespace E2E -Name U -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(System.IntPtr v);
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, System.UIntPtr extra);
[DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, uint data, System.UIntPtr extra);
[DllImport("user32.dll")] public static extern uint GetDpiForWindow(System.IntPtr hwnd);
'@

# Physical pixels, same as hexgrab.
[E2E.U]::SetProcessDpiAwarenessContext([IntPtr]-4) | Out-Null

function Pump([int]$ms) { $end = [DateTime]::UtcNow.AddMilliseconds($ms); while ([DateTime]::UtcNow -lt $end) { [System.Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 10 } }
function Key([byte]$vk, [switch]$Up) { [E2E.U]::keybd_event($vk, 0, $(if ($Up) { 2 } else { 0 }), [UIntPtr]::Zero) }
function Hotkey { Key 0x5B; Key 0x10; Key 0x48; Key 0x48 -Up; Key 0x10 -Up; Key 0x5B -Up }
function LeftClick { [E2E.U]::mouse_event(0x02, 0, 0, 0, [UIntPtr]::Zero); [E2E.U]::mouse_event(0x04, 0, 0, 0, [UIntPtr]::Zero) }
function PixelAt([int]$x, [int]$y) {
    $bmp = [System.Drawing.Bitmap]::new(1, 1); $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($x, $y, 0, 0, $bmp.Size); $c = $bmp.GetPixel(0, 0); $g.Dispose(); $bmp.Dispose()
    '#{0:X2}{1:X2}{2:X2}' -f $c.R, $c.G, $c.B
}
# Other topmost windows (a maximized terminal, a toast) can cover the target mid-run.
# Re-raise it before each step and refuse to judge hexgrab if the target pixel is wrong.
function Front([string]$expected) {
    $form.TopMost = $false; $form.TopMost = $true; $form.Activate(); Pump 200
    $seen = PixelAt $cx $cy
    if ($seen -ne $expected) { throw "Target window is covered: expected $expected under the cursor, screen shows $seen. Close or minimize what is on top and rerun." }
}

$results = [System.Collections.Generic.List[string]]::new()
function Check($name, $ok, $detail) { $results.Add(("{0} {1} - {2}" -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $name, $detail)) }

$proc = Start-Process -FilePath $ExePath -PassThru
Start-Sleep -Milliseconds 1500

$form = [System.Windows.Forms.Form]::new()
$form.Text = 'hexgrab e2e target'
$form.FormBorderStyle = 'None'
$form.StartPosition = 'Manual'
$form.Location = [System.Drawing.Point]::new(300, 300)
$form.Size = [System.Drawing.Size]::new(500, 400)
$form.TopMost = $true
$form.BackColor = [System.Drawing.Color]::FromArgb(0x33, 0x66, 0xCC)
$script:clicks = 0
$form.add_MouseDown({ $script:clicks++ })
$form.Show()
Pump 500

$cx = $form.Left + 250; $cy = $form.Top + 200
$results.Add("info: target window DPI = $([E2E.U]::GetDpiForWindow($form.Handle)), cursor at ($cx,$cy) physical")

try {
    # 1. Pick with left click
    Set-Clipboard -Value 'sentinel'
    [E2E.U]::SetCursorPos($cx, $cy) | Out-Null; Pump 200
    Front '#3366CC'
    Hotkey; Pump 400
    LeftClick; Pump 600
    $clip = Get-Clipboard
    Check 'click copies HEX' ($clip -eq '#3366CC') "clipboard='$clip'"
    Check 'click swallowed' ($script:clicks -eq 0) "target saw $script:clicks mouse-down(s)"

    # 2. Esc cancels, clipboard untouched
    Set-Clipboard -Value 'sentinel'
    Front '#3366CC'
    Hotkey; Pump 400
    Key 0x1B; Key 0x1B -Up; Pump 400
    $clip = Get-Clipboard
    Check 'Esc cancels' ($clip -eq 'sentinel') "clipboard='$clip'"

    # 3. Enter picks
    Front '#3366CC'
    Hotkey; Pump 400
    Key 0x0D; Key 0x0D -Up; Pump 600
    $clip = Get-Clipboard
    Check 'Enter copies HEX' ($clip -eq '#3366CC') "clipboard='$clip'"

    # 4. Hooks are gone after a pick: a normal click reaches the target again
    Front '#3366CC'
    $script:clicks = 0
    LeftClick; Pump 400
    Check 'hooks removed after pick' ($script:clicks -eq 1) "target saw $script:clicks mouse-down(s)"

    # 5. Different colour after the form changes under a still cursor (polling, not mouse-move)
    Set-Clipboard -Value 'sentinel'
    Front '#3366CC'
    Hotkey; Pump 300
    $form.BackColor = [System.Drawing.Color]::FromArgb(0xE9, 0x1E, 0x63); Pump 400
    LeftClick; Pump 600
    $clip = Get-Clipboard
    Check 'colour change under still cursor' ($clip -eq '#E91E63') "clipboard='$clip'"
}
finally {
    $form.Close()
    if (-not $proc.HasExited) { Stop-Process -Id $proc.Id }
}

$results
