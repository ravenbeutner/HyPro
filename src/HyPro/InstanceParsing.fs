module InstanceParsing 

open System.IO

open TransitionSystemLib.TransitionSystem
open TransitionSystemLib.SymbolicSystem
open TransitionSystemLib.BooleanProgramSystem

open FsOmegaLib.LTL

open Util
open HyperLTL
open HyperLTLVariants

let readAndParseExplicitInstance systemInputPaths formulaInputPath =
    let explicitTsList = 
        systemInputPaths 
        |> List.map (fun x -> 
                try 
                    File.ReadAllText  x
                with 
                | _ -> raise <| HyProException $"Could not open/read file %s{x}"
            )
        |> List.mapi (fun i s -> 
            match TransitionSystemLib.TransitionSystem.Parser.parseTransitionSystem s with 
                | Result.Ok x -> x 
                | Result.Error msg -> raise <| HyProException $"The %i{i}th explicit-state transition system could not be parsed: %s{msg}"
            )

    let propContent = 
        try 
            File.ReadAllText formulaInputPath
        with 
        | _ -> raise <| HyProException $"Could not open/read file %s{formulaInputPath}"


    let formula = 
        match HyperLTL.Parser.parseHyperLTL Util.ParserUtil.escapedStringParser propContent with
        | Result.Ok x -> x
        | Result.Error err -> raise <| HyProException $"The HyperLTL formula could not be parsed: %s{err}"

    explicitTsList, formula

let readAndParseSymbolicInstance systemInputPaths formulaInputPath =
    let systemList = 
        systemInputPaths 
        |> List.map (fun x -> 
                try 
                    File.ReadAllText  x
                with 
                | _ -> raise <| HyProException $"Could not open/read file %s{x}"
            )
        |> List.mapi (fun i s -> 
            match TransitionSystemLib.SymbolicSystem.Parser.parseSymbolicSystem s with 
            | Result.Ok x -> x 
            | Result.Error msg -> 
                raise <| HyProException $"The %i{i}th symbolic system could not be parsed: %s{msg}"
            )

    let propContent = 
        try 
            File.ReadAllText formulaInputPath
        with 
        | _ -> raise <| HyProException $"Could not open/read file %s{formulaInputPath}"

    let formula = 
        match HyperLTLVariants.Parser.parseSymbolicSystemHyperLTL propContent with
        | Result.Ok x -> x
        | Result.Error err -> 
            raise <| HyProException $"The HyperLTL formula could not be parsed: %s{err}"

    systemList, formula

let convertSymbolicSystemInstance (plist : list<SymbolicSystem>) (formula : SymbolicSystemHyperLTL) = 
    match SymbolicSystemHyperLTL.findError formula with 
    | None -> () 
    | Some msg -> 
        raise <| HyProException $"Error in the specification: %s{msg}"

    plist 
    |> List.iteri (fun i p -> 
        match SymbolicSystem.findError p with 
        | None -> () 
        | Some msg -> 
            raise <| HyProException $"Error in the %i{i}th system: %s{msg}"
        )

    if plist.Length <> 1 && plist.Length <> formula.QuantifierPrefix.Length then 
        raise <| HyProException $"Invalid number of programs"

    let nameMapping = 
        SymbolicSystemHyperLTL.quantifiedTraceVariables formula
        |> List.mapi (fun i x -> x, i)
        |> Map.ofList

    let unfoldRelationPredicate (atom : SymbolicSystemExpressionAtom)  = 
        match atom with 
        | UnaryPredicate (e, n) -> 
            LTL.Atom ((e, n))
        | RelationalEqualityPredicate(e1, n1, e2, n2) -> 

            let getSystem index = 
                if plist.Length = 1 then plist.[0] else plist.[index]

            let t1 = 
                try 
                    SymbolicSystem.inferTypeOfExpression (getSystem nameMapping.[n1]) e1
                with 
                | TypeInferenceException err -> 
                    raise <| HyProException $"Error when inferring the type of expression %s{Expression.print e1} in relation equality atom: %s{err}"

            let t2 = 
                try 
                    SymbolicSystem.inferTypeOfExpression (getSystem nameMapping.[n2]) e2
                with 
                | TypeInferenceException err -> 
                    raise <| HyProException $"Error when inferring the type of expression %s{Expression.print e2} in relation equality atom: %s{err}"
                
            let t = 
                match VariableType.intersectTypes t1 t2 with 
                    | Some x -> x 
                    | None -> 
                        raise <| HyProException $"Error during unfolding: Could not intersect types %s{VariableType.print t1} and %s{VariableType.print t2} of expressions %s{Expression.print e1} and %s{Expression.print e2}."

            t
            |> VariableType.allValues
            |> List.map (fun v -> 
                LTL.And(
                    LTL.Atom((Expression.Eq(e1, v |> VariableValue.toConstant |> Expression.Const), n1)),
                    LTL.Atom((Expression.Eq(e2, v |> VariableValue.toConstant |> Expression.Const), n2))
                )
            )
            |> fun l -> if List.isEmpty l then LTL.False else l |> List.reduce (fun x y -> LTL.Or(x, y)) 

    let unfoldedHyperLTL = 
        {
            HyperLTL.QuantifierPrefix = formula.QuantifierPrefix
            HyperLTL.LTLMatrix = 
                formula.LTLMatrix 
                |> LTL.bind unfoldRelationPredicate
        }

    let tsList = 
        if plist.Length = 1 then 
            // A single system where all traces are resolved on
            let atomList = 
                unfoldedHyperLTL.LTLMatrix
                |> LTL.allAtoms
                |> Set.toList
                |> List.map fst
                |> List.distinct
                
            atomList
            |> List.iter (fun (e : Expression) ->
                try
                    match SymbolicSystem.inferTypeOfExpression plist.[0] e with 
                    | BoolType -> ()
                    | t -> 
                        raise <| HyProException $"Expression %s{Expression.print e} used in the HyperLTL formula has non-boolean type %s{VariableType.print t}"
                with 
                | TypeInferenceException err -> 
                    raise <| HyProException $"Error when inferring type of expression %s{Expression.print e} used in the HyperLTL formula: %s{err}"
            )

            SymbolicSystem.convertSymbolicSystemToTransitionSystem plist.[0] atomList
            |> List.singleton
        else 
            // Multiple systems, so each is resolved on a separate system
            HyperLTL.quantifiedTraceVariables unfoldedHyperLTL
            |> List.map (fun pi -> 
                let atomList = 
                    unfoldedHyperLTL.LTLMatrix
                    |> LTL.allAtoms
                    |> Set.toList
                    |> List.filter (fun (_, pii) -> pi = pii)
                    |> List.map fst
                    |> List.distinct

                // Check that all atoms are well typed
                atomList
                |> List.iter (fun (e : Expression) ->
                    try
                        match SymbolicSystem.inferTypeOfExpression plist.[nameMapping[pi]] e with 
                        | BoolType -> ()
                        | t -> 
                            raise <| HyProException $"Expression %s{Expression.print e} used in the HyperLTL formula has non-boolean type %s{VariableType.print t}"
                    with 
                    | TypeInferenceException err -> 
                        raise <| HyProException $"Error when inferring type of expression %s{Expression.print e} used in the HyperLTL formula: %s{err}"
                )

                SymbolicSystem.convertSymbolicSystemToTransitionSystem plist.[nameMapping[pi]] atomList
            )

    let renamingMap = 
        unfoldedHyperLTL.LTLMatrix
        |> LTL.allAtoms
        |> Set.toList
        |> List.map fst
        |> List.distinct
        |> List.mapi (fun i x -> x, "a" + string(i))
        |> Map.ofList

    let mappedTs = 
        tsList
        |> List.map (TransitionSystemLib.TransitionSystem.TransitionSystem.mapAPs (fun x -> renamingMap[x]))
        
    let mappedFormula = 
        unfoldedHyperLTL
        |> HyperLTL.map (fun x -> renamingMap[x])

    mappedTs, mappedFormula


let readAndParseBooleanProgramInstance systemInputPaths formulaInputPath =
    let programList = 
        systemInputPaths 
        |> List.map (fun x -> 
                try 
                    File.ReadAllText  x
                with 
                    | _ -> 
                        raise <| HyProException $"Could not open/read file %s{x}"
            )
        |> List.mapi (fun i s -> 
            match TransitionSystemLib.BooleanProgramSystem.Parser.parseBooleanProgram s with 
            | Result.Ok x -> x 
            | Result.Error msg -> raise <| HyProException $"The %i{i}th boolean program could not be parsed: %s{msg}"
            )

    let propContent = 
        try 
            File.ReadAllText formulaInputPath
        with 
        | _ -> raise <| HyProException $"Could not open/read file %s{formulaInputPath}"


    let formula = 
        match HyperLTLVariants.Parser.parseBooleanProgramHyperLTL propContent with
        | Result.Ok x -> x
        | Result.Error err -> 
            raise <| HyProException $"The HyperQPTL formula could not be parsed: %s{err}"

    programList, formula

let convertBooleanProgramInstance (progList : list<BooleanProgram>) (formula : BooleanProgramHyperLTL) = 
    match BooleanProgramHyperLTL.findError formula with 
    | None -> () 
    | Some msg -> 
        raise <| HyProException $"Error in the specification: %s{msg}"

    progList 
    |> List.iteri (fun i p -> 
        match BooleanProgram.findError p with 
        | None -> () 
        | Some msg -> 
            raise <| HyProException $"Error in the %i{i}th system: %s{msg}"
        )

    if progList.Length <> 1 && progList.Length <> formula.QuantifierPrefix.Length then 
        raise <| HyProException $"Invalid number of programs"

    let nameMapping = 
        BooleanProgramHyperLTL.quantifiedTraceVariables formula
        |> List.mapi (fun i x -> x, i)
        |> Map.ofList


    let unfoldedHyperLTL = 
        {
            HyperLTL.QuantifierPrefix = formula.QuantifierPrefix
            HyperLTL.LTLMatrix = 
                formula.LTLMatrix 
                |> LTL.map (fun (x, i, pi) -> 
                    (x, i), pi
                    )
        }

    let tsList = 
        if progList.Length = 1 then 
            let prog = progList[0]

            let relevantAps =
                unfoldedHyperLTL.LTLMatrix
                |> LTL.allAtoms
                |> Set.toList
                |> List.map fst
                |> List.distinct

            relevantAps
            |> List.iter (fun (v, i) ->
                if prog.DomainMap.ContainsKey v |> not then 
                    raise <| HyProException $"AP {{%s{v} @ %i{i}}} is used in the HyperLTL property but variable %s{v} does not exists in the program"
                
                if prog.DomainMap.[v] <= i then
                    raise <| HyProException $"AP {{%s{v} @ %i{i}}} is used in the HyperLTL property but variable %s{v} does has only %i{prog.DomainMap.[v]} bits"
                )

            BooleanProgram.convertBooleanProgramToTransitionSystem prog relevantAps
            |> List.singleton
        else 
            HyperLTL.quantifiedTraceVariables unfoldedHyperLTL
            |> List.map (fun pi ->   
                let relevantAps = 
                    unfoldedHyperLTL.LTLMatrix
                    |> LTL.allAtoms
                    |> Set.toList
                    |> List.filter (fun (_, pii) -> pi = pii)
                    |> List.map fst
                    |> List.distinct
                    
                relevantAps
                |> List.iter (fun (v, j) ->
                    if progList.[nameMapping[pi]].DomainMap.ContainsKey v |> not then 
                        raise <| HyProException $"AP {{%s{v} @ %i{j}}}_%s{pi} is used in the HyperLTL property but variable %s{v} does not exists in the program for variable %s{pi}"
                    
                    if progList.[nameMapping[pi]].DomainMap.[pi] <= j then
                        raise <| HyProException $"AP {{%s{v} @ %i{j}}}_%s{pi} is used in the HyperLTL property but variable %s{v} in the program for variable %s{pi} has only %i{progList.[nameMapping[pi]].DomainMap.[pi]} bits"
                    )
                
                BooleanProgram.convertBooleanProgramToTransitionSystem progList.[nameMapping[pi]] relevantAps
            )
  
    let mappedTs = 
        tsList
        |> List.map (TransitionSystem.mapAPs (fun (x, i) -> x + "@" + string(i)))
            
    let mappedFormula = 
        unfoldedHyperLTL
        |> HyperLTL.map (fun (x, i) -> x + "@" + string(i))

    mappedTs, mappedFormula