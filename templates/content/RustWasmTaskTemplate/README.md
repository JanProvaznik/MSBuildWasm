# MSBuild Wasm - Rust Task Template

Template for creating a Rust WebAssembly task for MSBuild.


1. Use the Rust template add logic for the task.
2. Compile to WebAssembly with Cargo `cargo b --target wasm32-wasi --release --manifest-path=rust_template/Cargo.toml`
3. Register the task in the project file
```xml
<UsingTask TaskName="Template" AssemblyFile="$(MSBuildWasmAssembly)" TaskFactory="WasmTaskFactory" Condition="$(MSBuildWasmAssembly) != ''">
    <Task>rust_template\target\wasm32-wasi\release\rust_template.wasm</Task>
</UsingTask>
```
4. Use the task in the project file in a target

```xml
<Target Name="RunWasmTask">
<Template TestRequiredProperty="hello">
      <Output TaskParameter="TestOutputProperty" PropertyName="OutputProperty"/>
</Template>
</Target>
```
5. dotnet build

