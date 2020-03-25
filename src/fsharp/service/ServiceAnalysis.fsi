// Copyright (c) Microsoft Corporation.  All Rights Reserved.  See License.txt in the project root for license information.

namespace FSharp.Compiler.SourceCodeServices

open FSharp.Compiler.AbstractSyntax
open FSharp.Compiler.NameResolution
open FSharp.Compiler.Range

module public UnusedOpens =
    /// Get all unused open declarations in a file
    val getUnusedOpens : checkFileResults: FSharpCheckFileResults * getSourceLineStr: (int -> string) -> Async<range list>

module public SimplifyNames = 
   /// Data for use in finding unnecessarily-qualified names and generating diagnostics to simplify them
   type SimplifiableRange = {
        /// The range of a name that can be simplified
        Range: range
        /// The relative name that can be applied to a simplifiable name
        RelativeName: string
    }

    /// Get all ranges that can be simplified in a file
    val getSimplifiableNames : checkFileResults: FSharpCheckFileResults * getSourceLineStr: (int -> string) -> Async<SimplifiableRange list>

module public UnusedDeclarations = 
    /// Get all unused declarations in a file
    val getUnusedDeclarations : checkFileResults: FSharpCheckFileResults * isScriptFile: bool -> Async<range list>