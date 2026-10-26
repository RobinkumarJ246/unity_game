# glTF -> OBJ converter for the Loafbrr House & Props pack (CC0).
# All nodes in this file are translation-only (verified: no rotation/scale/matrix),
# so world positions are simple parent-chain offsets.
$ErrorActionPreference = "Stop"
$gltfPath = "C:\Unity_Games\ContentHouse\Assets\Loafbrr\HouseAndPropsAssetPack.gltf"
$outDir    = "C:\Unity_Games\ContentHouse\Assets\Loafbrr\OBJ"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$raw = [System.IO.File]::ReadAllText($gltfPath)
$bufMatch = [regex]::Match($raw, 'data:application/octet-stream;base64,([A-Za-z0-9+/=]+)')
if (-not $bufMatch.Success) { throw "no embedded buffer found" }
$buffer = [System.Convert]::FromBase64String($bufMatch.Groups[1].Value)
Write-Output "buffer decoded: $($buffer.Length) bytes"

$json = $raw.Replace($bufMatch.Value, "")
$doc = $json | ConvertFrom-Json

$meshes = $doc.meshes; $nodes = $doc.nodes; $access = $doc.accessors; $views = $doc.bufferViews

function ReadAccessor($acc, $compType, $vecLen) {
    $view = $views[[int]$acc.bufferView]
    $base = [int]$view.byteOffset + [int]$acc.byteOffset
    $count = [int]$acc.count
    if ($compType -eq 5126) {
        $arr = New-Object 'float[]' ($count*$vecLen)
        [System.Buffer]::BlockCopy($buffer, $base, $arr, 0, $count*$vecLen*4)
        return ,$arr
    } elseif ($compType -eq 5123) {
        $arr = New-Object 'UInt16[]' $count
        [System.Buffer]::BlockCopy($buffer, $base, $arr, 0, $count*2)
        return ,$arr
    } elseif ($compType -eq 5125) {
        $arr = New-Object 'UInt32[]' $count
        [System.Buffer]::BlockCopy($buffer, $base, $arr, 0, $count*4)
        return ,$arr
    }
    throw "unsupported componentType $compType"
}

$skipPattern = '^Road|^House\d+-colonly$'
$script:bounds = @{}
$script:written = 0

function EmitNode($nodeIdx, $ox, $oy, $oz) {
    $n = $nodes[$nodeIdx]
    if ($n.translation) {
        foreach ($t in $n.translation) { }
        $tt = @($n.translation | ForEach-Object { [double]$_ })
        $ox += [double]$tt[0]; $oy += [double]$tt[1]; $oz += [double]$tt[2]
    }
    if ($n.children) { foreach ($c in $n.children) { EmitNode ([int]$c) $ox $oy $oz } }
    if ($null -eq $n.mesh) { return }
    $name = $n.name
    if (-not $name) { $name = "node_$nodeIdx" }
    if ($name -match $skipPattern) { return }

    $mesh = $meshes[[int]$n.mesh]
    $objPath = Join-Path $outDir ($name + ".obj")
    $sw = New-Object System.IO.StreamWriter($objPath, $false)
    $sw.WriteLine("# Loafbrr CC0 (https://loafbrr.itch.io/) - converted from glTF, transforms baked")
    $sw.WriteLine("o $name")
    $vBase = 1
    $mn0 = 1e9; $mn1 = 1e9; $mn2 = 1e9; $mx0 = -1e9; $mx1 = -1e9; $mx2 = -1e9

    foreach ($prim in $mesh.primitives) {
        $posAcc = $access[[int]$prim.attributes.POSITION]
        $pos = ReadAccessor $posAcc 5126 3
        $norm = $null
        if ($null -ne $prim.attributes.NORMAL) {
            $norm = ReadAccessor $access[[int]$prim.attributes.NORMAL] 5126 3
        }
        $uv = $null
        if ($null -ne $prim.attributes.TEXCOORD_0) {
            $uv = ReadAccessor $access[[int]$prim.attributes.TEXCOORD_0] 5126 2
        }
        $idxAcc = $access[[int]$prim.indices]
        $idx = ReadAccessor $idxAcc ([int]$idxAcc.componentType) 1

        $vc = [int]$posAcc.count
        for ($i=0; $i -lt $vc; $i++) {
            $px = [double]$pos[$i*3]   + $ox
            $py = [double]$pos[$i*3+1] + $oy
            $pz = [double]$pos[$i*3+2] + $oz
            $sw.WriteLine(("v {0:r} {1:r} {2:r}" -f $px,$py,$pz))
            if ($px -lt $mn0) { $mn0 = $px }; if ($px -gt $mx0) { $mx0 = $px }
            if ($py -lt $mn1) { $mn1 = $py }; if ($py -gt $mx1) { $mx1 = $py }
            if ($pz -lt $mn2) { $mn2 = $pz }; if ($pz -gt $mx2) { $mx2 = $pz }
        }
        if ($uv) {
            $uc = [int]($uv.Length/2)
            for ($i=0; $i -lt $uc; $i++) {
                $sw.WriteLine(("vt {0:r} {1:r}" -f $uv[$i*2], $uv[$i*2+1]))
            }
        }
        if ($norm) {
            for ($i=0; $i -lt $vc; $i++) {
                $sw.WriteLine(("vn {0:r} {1:r} {2:r}" -f $norm[$i*3],$norm[$i*3+1],$norm[$i*3+2]))
            }
        }
        $matName = "mat"
        if ($null -ne $prim.material) { $matName = $doc.materials[[int]$prim.material].name; if (-not $matName) { $matName = "mat" } }
        $sw.WriteLine("usemtl $matName")
        for ($i=0; $i -lt $idx.Length; $i+=3) {
            $a = [int]$idx[$i]+$vBase; $b = [int]$idx[$i+1]+$vBase; $c = [int]$idx[$i+2]+$vBase
            if ($norm -and $uv) { $sw.WriteLine("f $a/$a/$a $b/$b/$b $c/$c/$c") }
            elseif ($norm)       { $sw.WriteLine("f $a//$a $b//$b $c//$c") }
            elseif ($uv)         { $sw.WriteLine("f $a/$a $b/$b $c/$c") }
            else                 { $sw.WriteLine("f $a $b $c") }
        }
        $vBase += $vc
    }
    $sw.Close()
    $script:written++
    if ($name -match '^(House\d+)') {
        $hn = $matches[1]
        if (-not $script:bounds.ContainsKey($hn)) { $script:bounds[$hn] = @(1e9,1e9,1e9,-1e9,-1e9,-1e9) }
        $b = $script:bounds[$hn]
        if ($mn0 -lt $b[0]) { $b[0] = $mn0 }; if ($mx0 -gt $b[3]) { $b[3] = $mx0 }
        if ($mn1 -lt $b[1]) { $b[1] = $mn1 }; if ($mx1 -gt $b[4]) { $b[4] = $mx1 }
        if ($mn2 -lt $b[2]) { $b[2] = $mn2 }; if ($mx2 -gt $b[5]) { $b[5] = $mx2 }
        $script:bounds[$hn] = $b
    }
}

$scene = $doc.scenes[[int]$doc.scene]
foreach ($n in $scene.nodes) { EmitNode ([int]$n) 0 0 0 }

$sum = @()
foreach ($k in ($script:bounds.Keys | Sort-Object)) {
    $b = $script:bounds[$k]
    $sum += [pscustomobject]@{
        house = $k
        minX=[math]::Round($b[0],1); minY=[math]::Round($b[1],1); minZ=[math]::Round($b[2],1)
        maxX=[math]::Round($b[3],1); maxY=[math]::Round($b[4],1); maxZ=[math]::Round($b[5],1)
        sizeX=[math]::Round($b[3]-$b[0],1); sizeY=[math]::Round($b[4]-$b[1],1); sizeZ=[math]::Round($b[5]-$b[2],1)
    }
}
$sum | ConvertTo-Json | Set-Content (Join-Path $outDir "house_bounds.json") -Encoding UTF8
$sum | Format-Table -AutoSize
Write-Output "OBJ files written: $script:written"
