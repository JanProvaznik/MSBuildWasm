# Examples of tasks in other languages used in .NET

- `ExampleProject` - project containing .csproj file importing the Wasm/WASI tasks

- `rust_hello`

## Using rust to create tasks:
1. go to it's directory and `cargo build --target wasm32-wasi --release`
2. find resulting .wasm file to be used as a task in `target/wasm32-wasi/release/name.wasm`
