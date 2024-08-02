# MSBuildWasm
**EXPERIMENTAL**

This package introduces the `WasmTaskFactory` which can be used to create MSBuild tasks from .wasm modules.

## Usage
0. To create a compatible Wasm module use the template in https://github.com/JanProvaznik/MSBuildWasm/tree/main/templates
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