module HyperLTL

open FsOmegaLib.LTL

open System
open System.IO

open Util

type TraceVariable = String

type TraceQuantifierType = 
    | FORALL 
    | EXISTS


type HyperLTL<'L when 'L: comparison> = 
    {
        QuantifierPrefix : list<TraceQuantifierType * TraceVariable>
        LTLMatrix : LTL<'L * TraceVariable>
    }
    
module HyperLTL =

    let quantifiedTraceVariables (hyperltl : HyperLTL<'L>) =
        hyperltl.QuantifierPrefix |> List.map snd
        
    let print (varNames : 'L -> String) (hyperltl : HyperLTL<'L>)  =
        let strWriter = new StringWriter()
        for t in hyperltl.QuantifierPrefix do
            match t with 
                | FORALL, x -> strWriter.Write("forall " + x + ".")
                | EXISTS, x -> strWriter.Write("exists " + x + ".")

        let varStringer (x, i) = "\"" + varNames x + "\"_" + i

        strWriter.Write(LTL.printInSpotFormat varStringer hyperltl.LTLMatrix)
        strWriter.ToString()
    

    let map f (formula : HyperLTL<'L>)  = 
        {
            QuantifierPrefix = formula.QuantifierPrefix
            LTLMatrix =    
                formula.LTLMatrix 
                |> LTL.map (fun (x, pi) -> (f x, pi))
        }

    let findError (hyperltl : HyperLTL<'L>) = 
        let traceVars = quantifiedTraceVariables hyperltl

        try 
            if traceVars |> set |> Set.count <> List.length traceVars then 
                raise <| HyProException $"Some trace variable is quantified more than once."

            LTL.allAtoms hyperltl.LTLMatrix
            |> Set.iter (fun (_, pi) -> 
                if List.contains pi traceVars |> not then 
                    raise <| HyProException $"Trace Variable %s{pi} is used but not defined in the prefix"
                )
            None 
        
        with 
            | HyProException msg -> Some msg

    let rec extractBlocks (qf : list<TraceQuantifierType * TraceVariable>) = 
        match qf with 
        | [] -> []
        | [(t, pi)] -> [t, [pi]]
        | (t, pi) :: qff  -> 
            match extractBlocks qff with 
            | (tt, r) :: re when t = tt -> (tt, pi :: r) :: re 
            | re -> (t, [pi]) :: re 


module Parser =
    open FParsec
    
    let private keywords =
        [
            "X"
            "G"
            "F"
            "U"
            "W"
            "R"
        ]
        
    let traceVarParser =
        pipe2
            letter
            (manyChars (letter <|> digit <|> pchar '-'))
            (fun x y -> string(x) + y)
            
   
    let hyperLTLQuantifierPrefixParser =
        let existsTraceParser = 
            skipString "exists " >>. spaces >>. traceVarParser .>> spaces .>> pchar '.'
            |>> fun pi -> EXISTS, pi

        let forallTraceParser = 
            skipString "forall " >>. spaces >>. traceVarParser .>> spaces .>> pchar '.'
            |>> fun pi -> FORALL, pi

        spaces >>.
        many1 (choice [existsTraceParser; forallTraceParser] .>> spaces)
        .>> spaces


    let private hyperLTLAtomParser atomParser =
        atomParser .>> pchar '_' .>>. traceVarParser
        

    let private hyperLTLParser (atomParser : Parser<'T, unit>) =     
        pipe2 
            hyperLTLQuantifierPrefixParser
            (FsOmegaLib.LTL.Parser.ltlParser (hyperLTLAtomParser atomParser))
            (fun x y -> {HyperLTL.QuantifierPrefix = x; LTLMatrix = y})
    
    let parseHyperLTL (atomParser : Parser<'T, unit>) s =    
        let full = hyperLTLParser atomParser .>> spaces .>> eof
        let res = run full s
        match res with
        | Success (res, _, _) -> Result.Ok res
        | Failure (err, _, _) -> Result.Error err

        