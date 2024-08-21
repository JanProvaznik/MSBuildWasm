# MSBuildWasm Template Package
**EXPERIMENTAL**

Rust template for creating [MSBuild Tasks](https://learn.microsoft.com/visualstudio/msbuild/task-writing) using Rust with MSBuildWasm package using this [spec](https://github.com/dotnet/msbuild/blob/main/documentation/specs/proposed/Wasm-tasks.md)

## Usage
- install `cargo` and the `wasm32-wasi` target
- `dotnet new rust.msbuild.task -o my_project` - creates a new .NET project which includes a template Rust task
- `dotnet build my_project` - runs MSBuild which will compile the Rust to .wasm and create a MSBuild task from it
