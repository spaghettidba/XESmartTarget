cd %~dp0\XESmartTarget.Core
nuget pack -Build -Properties "Configuration=Release;Platform=x86" -OutputDirectory .\nuget -Suffix "x86"
nuget pack -Build -Properties "Configuration=Release;Platform=x64" -OutputDirectory .\nuget -Suffix "x64"