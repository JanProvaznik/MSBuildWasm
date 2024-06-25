// use wasm_bindgen::prelude::*;
// use wasm_bindgen::{prelude::wasm_bindgen, JsValue};

use std::{fs::File, io::{Read, Write}};

// #[wasm_bindgen]
// pub fn execute(args: JsValue) -> bool {
#[no_mangle]
pub extern "C" fn execute() -> bool 
{
    // let args: Vec<String> = from_value(args).unwrap();
    // join and print
    // let _args = args.join(" ");
    // println!("{}", _args);
    // create file and write fizz to it
    let mut file = File::create("tmp/fizz.txt").unwrap();
    file.write_all(b"fizz").unwrap();

    // buzz
    let mut file2 = File::create("tmp/buzz.txt").unwrap();
    file2.write_all(b"buzz").unwrap();

    // merge the 2 files by reading them and writing to a new file
    let mut file3 = File::create("out/fizzbuzz.txt").unwrap();
    let mut fizz = File::open("tmp/fizz.txt").unwrap();
    let mut buzz = File::open("tmp/buzz.txt").unwrap();
    let mut fizzbuzz = String::new();
    fizz.read_to_string(&mut fizzbuzz).unwrap();
    buzz.read_to_string(&mut fizzbuzz).unwrap();
    file3.write_all(fizzbuzz.as_bytes()).unwrap();

    // print environment variables
    // let env = std::env::vars();
    // for (key, value) in env {
    //     println!("{}: {}", key, value);
    // }

    
    println!("bruh");
    return true;
}
