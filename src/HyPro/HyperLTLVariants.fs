module HyperLTLVariants 

open System

open FsOmegaLib.LTL

open TransitionSystemLib.SymbolicSystem

open HyperLTL

exception private NotWellFormedException of String

// ################################ HyperLTL for NuSMV programs #####################################

type SymbolicSystemExpressionAtom = 
    | UnaryPredicate of Expression * TraceVariable
    | RelationalEqualityPredicate  of Expression * TraceVariable * Expression * TraceVariable


type SymbolicSystemHyperLTL = 
    {
        QuantifierPrefix : list<TraceQuantifierType * TraceVariable>
        LTLMatrix : LTL<SymbolicSystemExpressionAtom>
    }

module SymbolicSystemHyperLTL = 
    let quantifiedTraceVariables (formula : SymbolicSystemHyperLTL) =
        formula.QuantifierPrefix
        |> List.map (fun (_, pi) -> pi)
        
    let findError (formula : SymbolicSystemHyperLTL) = 
        let traceVars = quantifiedTraceVariables formula

        try 
            if traceVars |> set |> Set.count <> List.length traceVars then 
                raise <| NotWellFormedException $"Some trace variable is used more than once."

            LTL.allAtoms formula.LTLMatrix
            |> Set.iter (fun x -> 
                match x with
                | UnaryPredicate (_, n) -> 
                    if List.contains n traceVars |> not then 
                        raise <| NotWellFormedException $"Trace Variable %s{n} is used but not defined in the prefix"

                | RelationalEqualityPredicate (_, n1, _, n2) -> 
                    if List.contains n1 traceVars |> not then 
                        raise <| NotWellFormedException $"Trace Variable %s{n1} is used but not defined in the prefix"

                    if List.contains n2 traceVars |> not then 
                        raise <| NotWellFormedException $"Trace Variable %s{n2} is used but not defined in the prefix"  
                )
            None 
        with 
            | NotWellFormedException msg -> Some msg

// ################################ END - HyperLTL for NuSMV programs #####################################

// ################################ HyperLTL for boolean programs #####################################


type BooleanProgramHyperLTL = 
    {
        QuantifierPrefix : list<TraceQuantifierType * TraceVariable>
        LTLMatrix : LTL<string * int * TraceVariable>
    }

module BooleanProgramHyperLTL = 
    let quantifiedTraceVariables (formula : BooleanProgramHyperLTL) =
        formula.QuantifierPrefix
        |> List.map (fun (_, pi) -> pi)
        
    let findError (formula : BooleanProgramHyperLTL) = 
        let traceVars = quantifiedTraceVariables formula

        try 
            if traceVars |> set |> Set.count <> List.length traceVars then 
                raise <| NotWellFormedException $"Some trace variable is used more than once."

            LTL.allAtoms formula.LTLMatrix
            |> Set.iter (fun (_, _, pi) -> 
                if List.contains pi traceVars |> not then 
                    raise <| NotWellFormedException $"Trace variable %s{pi} is used but not defined in the prefix"
                )
            None 
        with 
            | NotWellFormedException msg -> Some msg


// ################################ END - HyperLTL for boolean programs #####################################


module Parser = 
    open FParsec

    // ####################################################
    // Parsing of HyperLTL for symbolic systems

    let private symbolicSystemExpressionAtomParser = 
        let indexedExpressionParser =   
            tuple2
                (skipChar '{' >>. spaces >>. TransitionSystemLib.SymbolicSystem.Parser.expressionParser .>> spaces .>> skipChar '}')
                (spaces >>. skipChar '_' >>. spaces >>. HyperLTL.Parser.traceVarParser) 

        let unaryAtomParser = 
            indexedExpressionParser
            |>> UnaryPredicate

        let relationalAtomParser = 
            pipe2
                (indexedExpressionParser .>> spaces .>> skipChar '=')
                (spaces >>. indexedExpressionParser)
                (fun (e1, pi1) (e2, pi2) -> RelationalEqualityPredicate(e1, pi1, e2, pi2))

        attempt(relationalAtomParser) <|> unaryAtomParser

    let private symbolicSystemHyperLTLParser = 
        pipe2 
            HyperLTL.Parser.hyperLTLQuantifierPrefixParser
            (FsOmegaLib.LTL.Parser.ltlParser symbolicSystemExpressionAtomParser)
            (fun x y -> {SymbolicSystemHyperLTL.QuantifierPrefix = x; SymbolicSystemHyperLTL.LTLMatrix = y})

    let parseSymbolicSystemHyperLTL s =    
        let full = symbolicSystemHyperLTLParser .>> spaces .>> eof
        let res = run full s
        match res with
        | Success (res, _, _) -> Result.Ok res
        | Failure (err, _, _) -> Result.Error err


    // ####################################################
    // Parsing of HyperLTL for boolean systems

    let private relVarParserBit = 
        tuple3
            (pstring "{" >>. spaces >>. many1Chars letter .>> spaces .>> pchar '@' .>> spaces)
            (pint32 .>> spaces .>> pstring "}" .>> spaces .>> pchar '_')
            (HyperLTL.Parser.traceVarParser)

    let private booleanProgramHyperLTLParser = 
        pipe2 
            HyperLTL.Parser.hyperLTLQuantifierPrefixParser
            (FsOmegaLib.LTL.Parser.ltlParser relVarParserBit)
            (fun x y -> {BooleanProgramHyperLTL.QuantifierPrefix = x; LTLMatrix = y})

    let parseBooleanProgramHyperLTL s =    
        let full = booleanProgramHyperLTLParser .>> spaces .>> eof
        let res = run full s
        match res with
        | Success (res, _, _) -> Result.Ok res
        | Failure (err, _, _) -> Result.Error err