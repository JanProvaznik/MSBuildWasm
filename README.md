 Wasm/WASI for MSBuild
- MSBuild task is an unit of execution inside a build, [that can be created by users of MSBuild](https://learn.microsoft.com/en-us/visualstudio/msbuild/tutorial-custom-task-code-generation)
- experimental project exporing using Wasm/WASI to create MSBuild tasks
- Wasm/WASI supports multiple languages extending the notion of a MSBuild Task from a C# class to a Wasm module
    - Rust example tasks [examples/]
- The tasks run in a Wasm/WASI runtime - [Wasmtime](https://github.com/bytecodealliance/wasmtime) which sandboxes the execution from the rest of the system and files/directories a task allowed to touch have to be specified.

see [spec](https://github.com/dotnet/msbuild/pull/10259) for details

## User manual
Create a MSBuild task using Wasm/WASI toolchain.
1. install [wasi-sdk](https://github.com/WebAssembly/wasi-sdk), [cargo](https://doc.rust-lang.org/cargo/getting-started/installation.html), build the .dll for MSBuildWasm (e.g. by `dotnet publish src`)
2. copy *rust_template* from the examples folder and add your behavior to the `lib.rs` file, take care to specify the input/output parameters
3. compile with `cargo build --release --target wasm32-wasi`
2. in your project's `project.csproj` that you want to build with MSBuild include the task 
```xml
<UsingTask TaskName="MyWasmTask" AssemblyFile="MSBuildWasm.dll" TaskFactory="WasmTaskFactory">
<Task>your_module.wasm</Task>
</UsingTask>
```
3. use the task in a target
```xml
<Target Name="MyWasmTarget" AfterTargets="Build">
  <MyWasmTask Param="StringParam" Param2="true">
      <Output TaskParameter="Result" PropertyName="TaskResult"/>
  </MyWasmTask>
</Target>
```
4. `dotnet build`

Inputs and outputs from a tasks can be bools, strings and "ITaskItem" which is basically a file path.

[Writing tasks for MSBuild](https://learn.microsoft.com/en-us/visualstudio/msbuild/task-writing)