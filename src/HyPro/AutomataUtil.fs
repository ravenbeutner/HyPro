module AutomataUtil 

open FsOmegaLib.AutomatonSkeleton
open FsOmegaLib.NPA

let constructNpaDisjunction (autList : list<NPA<int, 'L>>) = 
    let autList = NPA.bringToSameAPs autList

    let npa = 
        {
            NPA.Skeleton = 
                {
                    AutomatonSkeleton.States =
                        autList
                        |> List.mapi (fun i npa -> 
                            npa.States |> Set.map (fun s -> i, s)
                            )
                        |> Set.unionMany
                    APs = autList.[0].APs
                    Edges = 
                        autList
                        |> List.mapi (fun i npa -> 
                            npa.Edges
                            |> Map.toSeq
                            |> Seq.map (fun (s, l) -> 
                                (i, s), l |> List.map (fun (g, t) -> g, (i, t))
                                )
                            )
                        |> Seq.concat
                        |> Map.ofSeq
                }
            InitialStates = 
                autList
                |> List.mapi (fun i npa -> 
                    npa.InitialStates |> Set.map (fun s -> i, s)
                    )
                |> Set.unionMany
            Color = 
                autList
                |> List.mapi (fun i npa -> 
                    npa.Color
                    |> Map.toSeq
                    |> Seq.map (fun (s, c) -> 
                        (i, s), c
                        )
                    )
                |> Seq.concat
                |> Map.ofSeq
        }

    npa
    |> NPA.convertStatesToInt
