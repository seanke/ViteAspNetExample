dotnet publish "./../WebApp/WebApp.csproj" --configuration Release /p:DebugType=None --output "./../.build/WebApp"
if($LASTEXITCODE -ne 0){
    throw
}

Compress-Archive -Path "./../.build/WebApp/*" -DestinationPath "./../.build/WebApp.zip" -Force

Remove-Item "./../.build/WebApp" -Recurse -ErrorAction SilentlyContinue