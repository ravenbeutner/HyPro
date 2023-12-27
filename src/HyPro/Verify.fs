module Verify 

open System.Collections.Generic

open TransitionSystemLib.TransitionSystem

open FsOmegaLib.DPA
open FsOmegaLib.Operations

open ParityGameLib.ParityGame

open Util
open SolverConfiguration
open HyperLTL
open ConstructParityGame

type AnalysisOptions = 
    {
        UseProphecies : bool 
        UseAllProphecies : bool  
    }

type VerificationResult = 
    | SAT 
    | UNSAT 
    | UNKNOWN

let verify config (options : AnalysisOptions) (systemMap : Map<TraceVariable, TransitionSystem<string>>) (hyperltl : HyperLTL<string>) =
    let sw = System.Diagnostics.Stopwatch()

    // Convert the prefix to a block prefix
    let blockPrefix = HyperLTL.extractBlocks hyperltl.QuantifierPrefix

    if List.length blockPrefix > 2 then 
        raise <| HyProException "Only applicable to \\forall^*\\exists^* properties"

    let universalTraceVariables = 
        if fst blockPrefix.[0] = FORALL then 
            blockPrefix.[0]
            |> snd
            |> set
        else 
            if List.length blockPrefix <> 1 then 
                raise <| HyProException "Only applicable to \\forall^*\\exists^* properties"
            Set.empty

    
    sw.Restart()
    let dpa =
        match FsOmegaLib.Operations.LTLConversion.convertLTLtoDPA config.Debug config.SolverConfig.MainPath config.SolverConfig.Ltl2tgbaPath hyperltl.LTLMatrix with 
        | Success aut -> aut 
        | Fail err -> 
            config.Logger.LogN err.DebugInfo
            raise <| HyProException err.Info

    config.Logger.LogN $"Computed DPA with %i{dpa.States.Count} states in %i{sw.ElapsedMilliseconds}ms"

    // ######################################################################

    // We compile to a PG once to get all positions of interest for prophecies

    sw.Restart()
    let pg, initState = ConstructParityGame.compileToParityGame config systemMap universalTraceVariables dpa Map.empty
    
    config.Logger.LogN $"Constructed initial (prophecy-free) PG with %i{pg.Properties.Count} states in %i{sw.ElapsedMilliseconds}ms"

    sw.Restart()
    let sol = ParityGameLib.ParityGameSolverOink.solveWithOink config.SolverConfig.MainPath config.SolverConfig.OinkPath pg
    config.Logger.LogN $"Solved PG in %i{sw.ElapsedMilliseconds}ms"

    if sol.WinnerMap.[initState] = PlayerZero then 
        // Could be verified without prophecies. The property holds
        config.Logger.LogN "Successfully verified property without prophecies"

        SAT
    else if not options.UseProphecies then 
        config.Logger.LogN "Could not verify property without prophecies and no prophecies are desired: UNKNOWN"

        UNKNOWN
    else    
        config.Logger.LogN "Could not verify property without prophecies, start prophecy construction"
        // Need to use prophecies
        sw.Restart()

        // We first collect all locations for which prophecies are relevant
        let allProphecyLocations = 
            pg.Properties.Keys 
            |> Seq.choose (function 
                | UpdateStage (gs, _, e) -> 
                    Some (e.NextExistentialStates, gs.MainDpaState)
                | _ -> None
            )
            
        config.Logger.LogN $"We need at most %i{Seq.length allProphecyLocations} prophecies"

        let prophecyHasher = new Dictionary<_,_>()

        // ==========================================================================================
        // Compute the prophecy automaton for the fixed positions
        let getProphecy (systemStates: Map<TraceVariable,int>) (dpaState: int) = 
            // We can assume that the id in this automaton is "p, 0"
            if prophecyHasher.ContainsKey (systemStates, dpaState) then 
                prophecyHasher.[(systemStates, dpaState)]
            else 
                // Generate the prophecy
                let prophecyNba = ProphecyConstruction.generateProphecy systemMap universalTraceVariables dpa systemStates dpaState

                let prophecyDpa = 
                    match FsOmegaLib.Operations.AutomatonConversions.convertToDPA config.Debug config.SolverConfig.MainPath config.SolverConfig.AutfiltPath Effort.HIGH prophecyNba with 
                    | Success x -> x 
                    | Fail err -> raise <| HyProException $"Could not convert prophecy to DPA: %s{err.Info}"

                
                prophecyHasher.Add((systemStates, dpaState), prophecyDpa)

                prophecyDpa

            
        // ==========================================================================================

        let hasher = new Dictionary<_,_>()

        let checkIfPropheciesSuffice (c : seq<Map<TraceVariable,int> * int>) = 
            config.Logger.LogN $"Checking Prophecies for %A{c}"
            // Generate the prophecies for each location in c

            let prophecyMap = 
                c 
                |> Seq.toList
                |> List.sortBy id
                |> List.mapi (fun i (systemStates, dpaState: int) -> 
                    let pname = "p" + string(i)

                    pname, getProphecy systemStates dpaState
                )
                |> Map.ofList

            // We hash on the set of positive automata oposed to the prophecy automata as they are indepndt of the name of the prophecies
            let r = 
                if hasher.ContainsKey prophecyMap then 
                    hasher.[prophecyMap]
                else 

                    let pg, initState = ConstructParityGame.compileToParityGame config systemMap universalTraceVariables dpa prophecyMap
                    
                    let sol = ParityGameLib.ParityGameSolverOink.solveWithOink config.SolverConfig.MainPath config.SolverConfig.OinkPath pg

                    let res = 
                        if sol.WinnerMap.[initState] = PlayerZero then 
                            // Could be verified without prophecies. The property holds
                            config.Logger.LogN "Successfully verified property with prophecies"
                            Some prophecyMap
                        else 
                            config.Logger.LogN "Could not verify property without prophecies and no prophecies are desired: UNKNOWN"
                            None

                    hasher.Add(prophecyMap, res)
                    res

            r

        let result = 
            if options.UseAllProphecies then 
                checkIfPropheciesSuffice allProphecyLocations
            else 
                let rec searchSizes (sizes) = 
                    match sizes with 
                    | [] -> None 
                    | x :: xs -> 
                        let rec searchCandidates (candidates) = 
                            match candidates with 
                            | [] -> None 
                            | y :: ys -> 
                                match checkIfPropheciesSuffice y with 
                                | Some z -> Some z 
                                | None -> searchCandidates ys

                        let candidates = 
                            Util.powersetOfFixedSize allProphecyLocations x 
                            |> Seq.toList

                        match searchCandidates candidates with 
                        | Some z -> Some z 
                        | None -> searchSizes xs

                searchSizes [1..Seq.length allProphecyLocations]

        match result with 
        | None -> 
            config.Logger.LogN "\n===== Property does not hold (assuming it had a safety matrix) =====\n"
            UNSAT
        | Some x -> 
            config.Logger.LogN "\n===== Property holds with prophecies =====\n"

            for y in x.Values do   
                let s = DPA.toHoaString string (fun (a, b) -> a + "_" + b) y 
                config.Logger.LogN $"{s}\n\n"

            SAT
