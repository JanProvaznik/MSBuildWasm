use std::{fs::File, io::{Read, Write}};

#[no_mangle]
pub extern "C" fn execute()
{
    // return;
    // create file and write fizz to it
    let mut file = File::create("fizz.txt").unwrap();
    file.write_all(b"fizz").unwrap();

    // buzz
    let mut file2 = File::create("buzz.txt").unwrap();
    file2.write_all(b"buzz").unwrap();

    // merge the 2 files by reading them and writing to a new file
    let mut file3 = File::create("/out/fizzbuzz.txt").unwrap();
    let mut fizz = File::open("fizz.txt").unwrap();
    let mut buzz = File::open("buzz.txt").unwrap();
    let mut fizzbuzz = String::new();
    fizz.read_to_string(&mut fizzbuzz).unwrap();
    buzz.read_to_string(&mut fizzbuzz).unwrap();
    file3.write_all(fizzbuzz.as_bytes()).unwrap();
}
