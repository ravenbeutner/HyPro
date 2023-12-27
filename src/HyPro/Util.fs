module Util 

open System
open System.Collections.Generic

exception HyProException of String 

let rec cartesianProduct (LL: list<seq<'a>>) =
    match LL with
    | [] -> Seq.singleton []
    | L :: Ls ->
        seq {
            for x in L do
                for xs in cartesianProduct Ls -> x :: xs
        }
        
let cartesianProductMap (m : Map<'A, Set<'B>>) =
    let keysAsList = Seq.toList m.Keys

    keysAsList
    |> Seq.toList
    |> List.map (fun x -> m.[x] |> seq)
    |> cartesianProduct
    |> Seq.map (fun x -> 
        List.zip keysAsList x
        |> Map
        )

let rec powersetOfFixedSize (s : seq<'a>) n =
    if n <= 0 then 
        Seq.singleton Set.empty
    else 
        let recres = powersetOfFixedSize s (n-1)

        recres
        |> Seq.collect (fun x -> 
            s 
            |> Seq.filter (fun y -> Set.contains y x |> not)
            |> Seq.map (fun y -> Set.add y x)
            )
        |> Seq.distinct

let dictToMap (d : Dictionary<'A, 'B>) = 
    d 
    |> Seq.map (fun x -> x.Key, x.Value)
    |> Map.ofSeq

module ParserUtil = 
    open FParsec
    
    let escapedStringParser : Parser<string, unit> =
        let escapedCharParser : Parser<string, unit> =  
            anyOf "\"\\/bfnrt"
            |>> fun x -> 
                match x with
                | 'b' -> "\b"
                | 'f' -> "\u000C"
                | 'n' -> "\n"
                | 'r' -> "\r"
                | 't' -> "\t"
                | c   -> string c

        between
            (pchar '"')
            (pchar '"')
            (stringsSepBy (manySatisfy (fun c -> c <> '"' && c <> '\\')) (pstring "\\" >>. escapedCharParser))

let mergeMaps (map1) map2 = 
    Seq.append (Map.toSeq map1) (Map.toSeq map2)
    |> Map.ofSeq
