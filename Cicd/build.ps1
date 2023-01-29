dotnet publish "./../WebVite/WebVite.csproj" --configuration Release /p:DebugType=None --output "./../.build/WebVite"
if($LASTEXITCODE -ne 0){
    throw
}

Compress-Archive -Path "./../.build/WebVite/*" -DestinationPath "./../.build/WebVite.zip" -Force

Remove-Item "./../.build/WebVite" -Recurse -ErrorAction SilentlyContinue