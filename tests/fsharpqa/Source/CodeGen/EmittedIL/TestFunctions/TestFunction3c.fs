// #NoMono #NoMT #CodeGen #EmittedIL #NETFX20Only #NETFX40Only 
#light

let TestFunction1() =
    printfn "Hello";
    printfn "World";
    3+4
    
let TestFunction3c() =
    try 
       let x = TestFunction1()
       failwith "hello"
    with Failure msg when msg = "hello" -> 
       printfn "World"        
