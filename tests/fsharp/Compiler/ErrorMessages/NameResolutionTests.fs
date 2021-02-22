// Copyright (c) Microsoft Corporation.  All Rights Reserved.  See License.txt in the project root for license information.

namespace FSharp.Compiler.UnitTests

open NUnit.Framework
open FSharp.Compiler.SourceCodeServices

[<TestFixture>]
module NameResolutionTests =

    [<Test>]
    let FieldNotInRecord () =
        CompilerAssert.TypeCheckSingleError
            """
type A = { Hello:string; World:string }
type B = { Size:int; Height:int }
type C = { Wheels:int }
type D = { Size:int; Height:int; Walls:int }
type E = { Unknown:string }
type F = { Wallis:int; Size:int; Height:int; }

let r:F = { Size=3; Height=4; Wall=1 }
            """
            FSharpErrorSeverity.Error
            1129
            (9, 31, 9, 35)
            "The record type 'F' does not contain a label 'Wall'. Maybe you want one of the following:\r\n   Wallis"

    [<Test>]
    let RecordFieldProposal () =
        CompilerAssert.TypeCheckSingleError
            """
type A = { Hello:string; World:string }
type B = { Size:int; Height:int }
type C = { Wheels:int }
type D = { Size:int; Height:int; Walls:int }
type E = { Unknown:string }
type F = { Wallis:int; Size:int; Height:int; }

let r = { Size=3; Height=4; Wall=1 }
            """
            FSharpErrorSeverity.Error
            39
            (9, 29, 9, 33)
            "The record label 'Wall' is not defined. Maybe you want one of the following:\r\n   Walls\r\n   Wallis"