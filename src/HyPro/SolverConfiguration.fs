module SolverConfiguration 

open System
open System.IO

open Util
open FsOmegaLib.JSON 


type SolverConfiguration = 
    {
        MainPath : string
        Ltl2tgbaPath: string
        AutfiltPath: string
        OinkPath: string
    }

let private parseConfigFile (s : string) =
    match FsOmegaLib.JSON.Parser.parseJsonString s with 
    | Result.Error err -> raise <| HyProException $"Could not parse config file: %s{err}"
    | Result.Ok x -> 
        {
            MainPath = "./" 
            Ltl2tgbaPath = 
                (JSON.tryLookup "ltl2tgba" x)
                |> Option.defaultWith (fun _ -> raise <| HyProException "No field 'ltl2tgba' found in paths.json")
                |> JSON.tryGetString
                |> Option.defaultWith (fun _ -> raise <| HyProException "Field 'ltl2tgba' must contain a string")
            AutfiltPath = 
                (JSON.tryLookup "autfilt" x)
                |> Option.defaultWith (fun _ -> raise <| HyProException "No field 'autfilt' found in paths.json")
                |> JSON.tryGetString
                |> Option.defaultWith (fun _ -> raise <| HyProException "Field 'autfilt' must contain a string")
            OinkPath = 
                (JSON.tryLookup "oink" x)
                |> Option.defaultWith (fun _ -> raise <| HyProException "No field 'oink' found in paths.json")
                |> JSON.tryGetString
                |> Option.defaultWith (fun _ -> raise <| HyProException "Field 'oink' must contain a string")
        }

let getSolverConfiguration() = 
    // By convention the paths.json file is located in the same directory as the HyPA executable
    let configPath = 
        System.IO.Path.Join [|System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location); "paths.json"|]
                     
    // Check if the path to the config file is valid , i.e., the file exists
    if System.IO.FileInfo(configPath).Exists |> not then 
        raise <| HyProException "The paths.json file does not exist in the same directory as the executable"            
    
    // Parse the config File
    let configContent = 
        try
            File.ReadAllText configPath
        with 
            | _ -> 
                raise <| HyProException "Could not open paths.json file"

    let solverConfig = parseConfigFile configContent

    if System.IO.FileInfo(solverConfig.Ltl2tgbaPath).Exists |> not then 
        raise <| HyProException "The given path to spot's ltl2tgba does not exist"

    if System.IO.FileInfo(solverConfig.AutfiltPath).Exists |> not then 
        raise <| HyProException "The given path to spot's autfilt does not exist"

    if System.IO.FileInfo(solverConfig.OinkPath).Exists |> not then 
        raise <| HyProException "The given path to oink does not exist"

    solverConfig


type Logger = 
    {
        Log : string -> unit
    }

    member this.LogN s = this.Log (s + "\n") 

type Configuration = 
    {
        SolverConfig : SolverConfiguration
        Debug : bool
        Logger : Logger
    }