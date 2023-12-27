
module Program

open System 

open TransitionSystemLib.TransitionSystem

open Util
open SolverConfiguration
open Verify
open CommandLineParser


let private run (args: array<string>) =

    let swtotal = System.Diagnostics.Stopwatch()
    let sw = System.Diagnostics.Stopwatch()
    swtotal.Start()

    // Parse the command line args
    let cmdArgs =
        match CommandLineParser.parseCommandLineArguments (Array.toList args) with
        | Result.Ok x -> x
        | Result.Error e ->
                raise <| HyProException $"%s{e}"

    let solverConfig = SolverConfiguration.getSolverConfiguration()         
    let config = 
        {
            Configuration.SolverConfig = solverConfig
            Debug = false
            Logger = 
                {
                    Logger.Log =    
                        fun s -> 
                            if cmdArgs.DebugOutputs then 
                                printf $"%s{s}"
                }    
        }

    sw.Restart()

    let tsList, hyperltl = 
        match cmdArgs.ExecMode with 
        | ExplicitInstance -> 
            let files = cmdArgs.InputFiles
            let propertyFile = files[files.Length - 1]
            let systemFiles = files[0..files.Length - 2]


            let tsList, hyperltl = InstanceParsing.readAndParseExplicitInstance systemFiles propertyFile

            tsList, hyperltl
        
        | NuSMVInstance -> 
            let files = cmdArgs.InputFiles
            let propertyFile = files[files.Length - 1]
            let systemFiles = files[0..files.Length - 2]


            let nusmvList, formula = InstanceParsing.readAndParseSymbolicInstance systemFiles propertyFile

            let tsList, hyperltl = InstanceParsing.convertSymbolicSystemInstance nusmvList formula

            tsList, hyperltl

        | BooleanProgramInstance -> 
            let files = cmdArgs.InputFiles
            let propertyFile = files[files.Length - 1]
            let systemFiles = files[0..files.Length - 2]


            let programList, formula = InstanceParsing.readAndParseBooleanProgramInstance systemFiles propertyFile

            let tsList, hyperltl = InstanceParsing.convertBooleanProgramInstance programList formula

            tsList, hyperltl

    let tsList = 
        if cmdArgs.ComputeBisimulation then     
            // Compute bisimulation quotient
            sw.Restart()
            let bisim = 
                tsList
                |> List.map (fun ts -> TransitionSystemLib.TransitionSystem.TransitionSystem.computeBisimulationQuotient ts |> fst)

            config.Logger.LogN $"Computed bisimulation quotient in %i{sw.ElapsedMilliseconds}ms"
            config.Logger.LogN $"System sizes: %A{tsList |> List.map (fun ts -> ts.States.Count)}"

            bisim        
        else 
            tsList

    
    let tsList, hyperltl = 
        if tsList |> List.forall (fun ts -> ts.InitialStates |> Set.count = 1) then 
            tsList, hyperltl
        else 
            // We add a dummy initial state to ensure that each system as a unique initial state
            let modTsList = 
                tsList
                |> List.map (fun ts -> 
                    let newState = (ts.States |> Set.maxElement) + 1

                    {
                        TransitionSystem.States = Set.add newState ts.States
                        InitialStates = Set.singleton newState
                        APs = ts.APs
                        Edges = 
                            ts.Edges
                            |> Map.add newState ts.InitialStates
                        ApEval = 
                            ts.ApEval
                            |> Map.add newState Set.empty
                    }
                
                    )

            let modHyperLTL = 
                { hyperltl with LTLMatrix = FsOmegaLib.LTL.X (hyperltl.LTLMatrix)}

            modTsList, modHyperLTL

    let sizes = tsList |> List.map (fun x -> x.States.Count)

    config.Logger.LogN $"Explicit-state size: %A{sizes}"
    config.Logger.LogN $"Obtained explicit-state representation in %i{sw.ElapsedMilliseconds}ms (~=%.2f{double(sw.ElapsedMilliseconds) / 1000.0}s)"
    sw.Restart()

    let systemMap = 
        if List.length tsList = 1 then 
            hyperltl.QuantifierPrefix
            |> List.map snd 
            |> List.map (fun x -> x, tsList.[0])
            |> Map.ofList
        else 
            if List.length tsList <> List.length hyperltl.QuantifierPrefix then 
                raise <| HyProException $"The number of systems and quantifiers must match"

            List.zip tsList hyperltl.QuantifierPrefix
            |> List.map (fun (ts, (_, pi)) -> pi, ts)
            |> Map.ofList


    let options = 
        {
            UseProphecies = cmdArgs.UseProphecies
            UseAllProphecies = cmdArgs.UseAllProphecies
        }

    let res = Verify.verify config options systemMap hyperltl

    match res with 
    | SAT -> 
        printfn "SAT"
    | UNSAT -> 
        printfn "UNSAT"
    | UNKNOWN -> 
        printfn "UNKNOWN"


    swtotal.Stop()
    config.Logger.LogN $"\nTotal time: %i{swtotal.ElapsedMilliseconds}ms (~=%.2f{double(swtotal.ElapsedMilliseconds) / 1000.0}s)"
    0

[<EntryPoint>]
let main args =
    try 
        run args
    with 
    | HyProException err -> 
        printfn "================= ERROR ================="
        printfn "%s" err
        printfn "================= END - ERROR ================="
        reraise()
    | e -> 
        printfn "Unexpected Error during the analysis:"
        printfn "%s" e.Message
        reraise()