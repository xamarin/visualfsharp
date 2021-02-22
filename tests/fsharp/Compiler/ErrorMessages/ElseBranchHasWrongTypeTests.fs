﻿// Copyright (c) Microsoft Corporation.  All Rights Reserved.  See License.txt in the project root for license information.

namespace FSharp.Compiler.UnitTests

open NUnit.Framework
open FSharp.TestHelpers
open FSharp.Compiler.SourceCodeServices

[<TestFixture>]
module ``Else branch has wrong type`` =

    [<Test>]
    let ``Else branch is int while if branch is string``() =
        CompilerAssert.TypeCheckSingleError
            """
let test = 100
let y =
    if test > 10 then "test"
    else 123
            """
            FSharpErrorSeverity.Error
            1
            (5, 10, 5, 13)
            "All branches of an 'if' expression must return values of the same type as the first branch, which here is 'string'. This branch returns a value of type 'int'."

    [<Test>]
    let ``Else branch is a function that returns int while if branch is string``() =
        CompilerAssert.TypeCheckSingleError
            """
let test = 100
let f x = test
let y =
    if test > 10 then "test"
    else f 10
            """
            FSharpErrorSeverity.Error
            1
            (6, 10, 6, 14)
            "All branches of an 'if' expression must return values of the same type as the first branch, which here is 'string'. This branch returns a value of type 'int'."


    [<Test>]
    let ``Else branch is a sequence of expressions that returns int while if branch is string``() =
        CompilerAssert.TypeCheckSingleError
            """
let f x = x + 4

let y =
    if true then
        ""
    else
        "" |> ignore
        (f 5)
            """
            FSharpErrorSeverity.Error
            1
            (9, 10, 9, 13)
            "All branches of an 'if' expression must return values of the same type as the first branch, which here is 'string'. This branch returns a value of type 'int'."


    [<Test>]
    let ``Else branch is a longer sequence of expressions that returns int while if branch is string``() =
        CompilerAssert.TypeCheckSingleError
            """
let f x = x + 4

let y =
    if true then
        ""
    else
        "" |> ignore
        let z = f 4
        let a = 3 * z
        (f a)
            """
            FSharpErrorSeverity.Error
            1
            (11, 10, 11, 13)
            "All branches of an 'if' expression must return values of the same type as the first branch, which here is 'string'. This branch returns a value of type 'int'."


    [<Test>]
    let ``Else branch context doesn't propagate into function application``() =
        CompilerAssert.TypeCheckSingleError
            """
let test = 100
let f x : string = x
let y =
    if test > 10 then "test"
    else
        f 123
            """
            FSharpErrorSeverity.Error
            1
            (7, 11, 7, 14)
            "This expression was expected to have type\n    'string'    \nbut here has type\n    'int'    "

    [<Test>]
    let ``Else branch context doesn't propagate into function application even if not last expr``() =
        CompilerAssert.TypeCheckSingleError
            """
let test = 100
let f x = printfn "%s" x
let y =
    if test > 10 then "test"
    else
        f 123
        "test"
            """
            FSharpErrorSeverity.Error
            1
            (7, 11, 7, 14)
            "This expression was expected to have type\n    'string'    \nbut here has type\n    'int'    "

    [<Test>]
    let ``Else branch context doesn't propagate into for loop``() =
        CompilerAssert.TypeCheckSingleError
            """
let test = 100
let list = [1..10]
let y =
    if test > 10 then "test"
    else
        for (x:string) in list do
            printfn "%s" x

        "test"
            """
            FSharpErrorSeverity.Error
            1
            (7, 14, 7, 22)
            "This expression was expected to have type\n    'int'    \nbut here has type\n    'string'    "

    [<Test>]
    let ``Else branch context doesn't propagate to lines before last line``() =
        CompilerAssert.TypeCheckSingleError
            """
let test = 100
let list = [1..10]
let y =
    if test > 10 then "test"
    else
        printfn "%s" 1

        "test"
            """
            FSharpErrorSeverity.Error
            1
            (7, 22, 7, 23)
            "This expression was expected to have type\n    'string'    \nbut here has type\n    'int'    "

    [<Test>]
    let ``Else branch should not have wrong context type``() =
        CompilerAssert.TypeCheckWithErrors
            """
let x = 1
let y : bool =
    if x = 2 then "A"
    else "B"
            """
            [| FSharpErrorSeverity.Error, 1, (4, 19, 4, 22), "The 'if' expression needs to have type 'bool' to satisfy context type requirements. It currently has type 'string'."
               FSharpErrorSeverity.Error, 1, (5, 10, 5, 13), "All branches of an 'if' expression must return values of the same type as the first branch, which here is 'bool'. This branch returns a value of type 'string'." |]


    [<Test>]
    let ``Else branch has wrong type in nested if``() =
        CompilerAssert.TypeCheckWithErrors
            """
let x = 1
if x = 1 then true
else
    if x = 2 then "A"
    else "B"
            """
            [| FSharpErrorSeverity.Error, 1, (5, 19, 5, 22), "All branches of an 'if' expression must return values of the same type as the first branch, which here is 'bool'. This branch returns a value of type 'string'."
               FSharpErrorSeverity.Error, 1, (6, 10, 6, 13), "All branches of an 'if' expression must return values of the same type as the first branch, which here is 'bool'. This branch returns a value of type 'string'."
               FSharpErrorSeverity.Warning, 20, (3, 1, 6, 13), "The result of this expression has type 'bool' and is implicitly ignored. Consider using 'ignore' to discard this value explicitly, e.g. 'expr |> ignore', or 'let' to bind the result to a name, e.g. 'let result = expr'." |]
