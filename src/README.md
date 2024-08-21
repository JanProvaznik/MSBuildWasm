# MSBuildWasm
**EXPERIMENTAL**

This package introduces the `WasmTaskFactory` which creates MSBuild tasks from .wasm modules.

## Usage
0. Create a compatible WASIp1 module [spec](https://github.com/dotnet/msbuild/blob/main/documentation/specs/proposed/Wasm-tasks.md), e.g. from a [template](https://github.com/JanProvaznik/MSBuildWasm/tree/main/templates/content/RustWasmTaskTemplate/rust_template)
1. In a the .proj file add 
```xml
<UsingTask TaskName="MyTask" AssemblyFile="$(MSBuildWasmAssembly)" TaskFactory="WasmTaskFactory" Condition="$(MSBuildWasmAssembly) != ''">
<Task>path/to/module.wasm</Task>
</UsingTask>
```
2. Use the task
```xml
<Target Name="MyTarget">
<MyTask/>
</Target>
```
3. `dotnet build`
