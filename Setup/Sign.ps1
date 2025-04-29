[CmdletBinding()]
Param(
    [Parameter(Mandatory=$True,Position=1)]
    [string]$InputFile,
    [Parameter(Mandatory=$False,Position=2)]
    [string]$OutputFile = $InputFile
)


if(-not (Test-Path c:\xesmarttarget\SignParams.ps1)) 
{
    Write-Warning "No code signing is applied to the .msi file."
    Write-Warning "You need to create a file called SignParams.ps1 and provide signing info."
    Move-Item $InputFile $OutputFile -Force
    exit
}

# read paramters
$signParams = get-content c:\xesmarttarget\SignParams.ps1 -Raw
Invoke-Expression $signParams

$params = $(
     'sign'
    ,'/f'
    ,('"' + $certPath + '"')
    ,'/p'
    ,('"' + $certPass + '"')
    ,'/sha1'
    ,$certSha
    ,'/t'
    ,('"' + $certTime + '"')
    ,'/d'
    ,'"XESmartTarget"'
    ,"/fd"
    ,"sha1"
)

& $signTool ($params + $InputFile)

Write-Output "Moving $InputFile --> $OutputFile"
Move-Item $InputFile $OutputFile -Force
