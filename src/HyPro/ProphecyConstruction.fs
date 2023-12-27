module ProphecyConstruction 

open System.Collections.Generic

open FsOmegaLib.SAT
open FsOmegaLib.AutomatonSkeleton
open FsOmegaLib.DPA
open FsOmegaLib.NPA

open TransitionSystemLib.TransitionSystem

open HyperLTL

type ProphecyVariable = string 

type ExtendedAlphabet<'L> = 
    | NormalAp of 'L * TraceVariable
    | ProphecyVariable of ProphecyVariable

type private ProductState<'T> = 
    | InitialState
    | PositiveDpaState of 'T
    | NegativeDpaState of 'T


/// Note that we offset the prophecy by one step, i.e., construct a NPA for F !(p <-> X proph), as we read the prophecies within the current step
let buildProphecyConstructNegated (prophecyDpa : DPA<int, 'L * TraceVariable>) (pname : ProphecyVariable) = 
    let indexOfProphecyVariable = prophecyDpa.APs.Length

    let newAps = (prophecyDpa.APs |> List.map NormalAp) @ [ProphecyVariable pname]

    let allStates = 
        Set.union (Set.map PositiveDpaState prophecyDpa.States) (Set.map NegativeDpaState prophecyDpa.States)
        |> Set.add InitialState

    let npa = 
        {
            NPA.Skeleton = 
                {
                    AutomatonSkeleton.States = allStates
                    APs = newAps
                    Edges = 
                        allStates
                        |> Seq.map (fun x -> 
                            let edges = 
                                match x with 
                                | InitialState -> 
                                    [
                                        ([[NL indexOfProphecyVariable]], PositiveDpaState prophecyDpa.InitialState)
                                        ([[PL indexOfProphecyVariable]], NegativeDpaState prophecyDpa.InitialState)
                                        (DNF.trueDNF, InitialState)
                                    ]
                                | PositiveDpaState s -> 
                                    prophecyDpa.Edges.[s]
                                    |> List.map (fun (g, x) -> g, PositiveDpaState x)
                                | NegativeDpaState s -> 
                                    prophecyDpa.Edges.[s]
                                    |> List.map (fun (g, x) -> g, NegativeDpaState x)
                            
                            x, edges
                            )
                        |> Map.ofSeq
                }
            InitialStates = Set.singleton InitialState
            Color = 
                allStates
                |> Seq.map (fun s -> 
                    let c = 
                        match s with 
                        | InitialState -> 1 // The initial state is not accepting, staying there will cause a violation
                        | PositiveDpaState q -> prophecyDpa.Color.[q]
                        | NegativeDpaState q -> prophecyDpa.Color.[q] + 1

                    s, c
                    )
                |> Map.ofSeq
        }

    npa
    |> NPA.convertStatesToInt


let generateProphecy (systemMap :  Map<TraceVariable, TransitionSystem<'L>>) (universalTraceVariables : Set<TraceVariable>) (dpa : DPA<int, 'L * TraceVariable>) (initStates : Map<TraceVariable, int>) (initDpaState : int) = 
    let newAps = 
        dpa.APs
        |> List.filter (fun (_, pi) -> Set.contains pi universalTraceVariables)

    // Map the old dp.APs into newAps
    let remappingMap = 
        newAps
        |> List.mapi (fun i x -> 
            (List.findIndex ((=) x) dpa.APs), i
            )
        |> Map.ofList

    let initStates = Seq.singleton (initStates, initDpaState)

    let queue = new Queue<_>(initStates)
    let allStates = new HashSet<_>(initStates)

    let edgeDict = new Dictionary<_,_>()
    let colorDict = new Dictionary<_,_>()

    while queue.Count <> 0 do 

        let n = queue.Dequeue()
        let stateMap, dpaState = n 

        let allSystemSuccesors = 
            stateMap
            |> Map.map (fun pi s -> 
                systemMap.[pi].Edges.[s]
                )
            |> Util.cartesianProductMap

        let fixingMap = 
            dpa.APs
            |> List.mapi (fun i (x, pi) -> 
                if Set.contains pi universalTraceVariables then 
                    None
                else    
                    // pi is quantified existentially, so we use the current system states to resolve it
                    let apIndex = systemMap.[pi].APs |> List.findIndex ((=) x)
                    let apEval = systemMap.[pi].ApEval.[stateMap.[pi]]

                    Some (i, Set.contains apIndex apEval)
                )
            |> List.choose id 
            |> Map.ofList

        let sucStates = 
            dpa.Edges.[dpaState]
            |> Seq.map (fun (guard, dpaState2) -> 

                let guardFixed = DNF.fixValues fixingMap guard

                if DNF.isSat guardFixed then 
                    let remappedGuard = DNF.map (fun x -> remappingMap.[x]) guardFixed

                    allSystemSuccesors
                    |> Seq.map (fun x -> remappedGuard, (x, dpaState2))
                else
                    Seq.empty
            )
            |> Seq.concat
            |> Seq.toList


        edgeDict.Add(n, sucStates)
        colorDict.Add(n, dpa.Color.[dpaState])

        for (_, x) in sucStates do 
            if allStates.Contains x |> not then 
                allStates.Add x |> ignore
                queue.Enqueue x


    let renaming = 
        allStates
        |> Seq.mapi (fun i x -> 
            x, i)
        |> Map.ofSeq    

    let edgeMap = 
        edgeDict
        |> Seq.map (fun x -> renaming.[x.Key], x.Value |> List.map (fun (g, y) -> g, (renaming.[y])))
        |> Map.ofSeq

    let colorMap = 
        colorDict
        |> Seq.map (fun x -> renaming.[x.Key], x.Value)
        |> Map.ofSeq

    let npa = 
        {
            NPA.Skeleton = 
                {
                    AutomatonSkeleton.States = Map.values renaming |> set
                    Edges = edgeMap
                    APs = newAps
                }
            InitialStates = initStates |> Seq.map (fun x -> renaming.[x]) |> set
            Color = colorMap
        }

    npa
