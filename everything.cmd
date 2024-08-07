rm *.nupkg
dotnet nuget locals global-packages --clear
dotnet build .\src\ -c Release
dotnet pack .\src\ -o .
dotnet add .\examples\ExampleProject\ExampleProject.csproj package MSBuildWasm -s .\ --prerelease
dotnet build .\examples\