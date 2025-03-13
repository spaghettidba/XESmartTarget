param (
    [Parameter(Mandatory=$false)]
    [string]$BuildVersion = "1.0.0.0",

    [Parameter(Mandatory=$false)]
    [string]$WixBinPath = "C:\Program Files (x86)\WiX Toolset v3.11\bin"
)

Set-Location $PSScriptRoot

#    qui genero ProductComponents perchè nel product.wxs c'era scritto cosi...
& "$WixBinPath\heat.exe" dir "..\XESmartTarget\bin\Release" `
    -gg -sfrag -sreg -srd -nologo `
    -cg "ProductComponents" `
    -dr "INSTALLFOLDER" `
    -out "$PSScriptRoot\ProductComponents.wxs" `
    -var "var.XESmartTargetDir"
#----------------------------------------------------------------
& "$WixBinPath\candle.exe" `
    -nologo `
    -out "$PSScriptRoot\candleout\" `
    -dXESmartTargetDir="..\XESmartTarget\bin\Release" `
    -dBuildVersion="$BuildVersion" `
    -dPlatform="x64" `
    "$PSScriptRoot\Product.wxs" `
    "$PSScriptRoot\ProductComponents.wxs" `
    -arch x64
#----------------------------------------------------------------
& "$WixBinPath\light.exe" `
    -out "C:\temp\XESmartTarget-$BuildVersion.msi" `
    "$PSScriptRoot\candleout\*.wixobj" `
    -ext WixUIExtension

Write-Host "MSI Generated: C:\temp\XESmartTarget-$BuildVersion.msi"
