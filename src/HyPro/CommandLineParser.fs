module CommandLineParser

open System


type ExecutionMode = 
    | ExplicitInstance
    | BooleanProgramInstance
    | NuSMVInstance

type CommandLineArguments = 
    {
        InputFiles : list<string>
        ExecMode : ExecutionMode
        DebugOutputs : bool

        ComputeBisimulation : bool
        UseProphecies : bool
        UseAllProphecies : bool
    }

    static member Default = 
        {
            InputFiles = []
            ExecMode = ExplicitInstance
            DebugOutputs = true

            ComputeBisimulation = true
            UseProphecies = false 
            UseAllProphecies = false
        }

let rec private splitByPredicate (f : 'T -> bool) (xs : list<'T>) = 
    match xs with 
    | [] -> [], []
    | x::xs -> 
        if f x then 
            [], x::xs 
        else 
            let r1, r2 = splitByPredicate f xs 
            x::r1, r2

let parseCommandLineArguments (args : list<String>) =
    let rec parseArgumentsRec (args : list<String>) (opt : CommandLineArguments) = 
        match args with 
        | [] -> Result.Ok opt
        | x::xs -> 
            match x with 
            | "--exp" -> 
                parseArgumentsRec xs {opt with ExecMode = ExplicitInstance}
            | "--nusmv" -> 
                parseArgumentsRec xs {opt with ExecMode = NuSMVInstance}
            | "--bp" -> 
                parseArgumentsRec xs {opt with ExecMode = BooleanProgramInstance}
            | "--debug" -> 
                parseArgumentsRec xs { opt with DebugOutputs = true}
            | "--no-bisim" -> 
                parseArgumentsRec xs { opt with ComputeBisimulation = false}
            | "--pro" -> 
                parseArgumentsRec xs { opt with UseProphecies = true}
            | "--all-pro" -> 
                parseArgumentsRec xs { opt with UseAllProphecies = true; UseProphecies = true}
            | s when s.StartsWith("-") -> 
                Result.Error ("Option " + x + " is not supported" )
            | _ ->
                let args, ys = splitByPredicate (fun (x : String) -> x.[0] = '-') args

                if List.length args < 2 then 
                    Result.Error "Must give at least two input files"
                else 
                    parseArgumentsRec ys {opt with InputFiles = args}
                    
        
    parseArgumentsRec args CommandLineArguments.Default
                                