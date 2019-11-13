﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.Quantum.QsCompiler.Optimizations

open System.Collections.Immutable
open Microsoft.Quantum.QsCompiler
open Microsoft.Quantum.QsCompiler.SyntaxExtensions
open Microsoft.Quantum.QsCompiler.Optimizations.Utils
open Microsoft.Quantum.QsCompiler.Optimizations.Tools
open Microsoft.Quantum.QsCompiler.Optimizations.VariableRenaming
open Microsoft.Quantum.QsCompiler.Optimizations.VariableRemoving
open Microsoft.Quantum.QsCompiler.Optimizations.StatementRemoving
open Microsoft.Quantum.QsCompiler.Optimizations.ConstantPropagation
open Microsoft.Quantum.QsCompiler.Optimizations.LoopUnrolling
open Microsoft.Quantum.QsCompiler.Optimizations.CallableInlining
open Microsoft.Quantum.QsCompiler.Optimizations.StatementReordering
open Microsoft.Quantum.QsCompiler.Optimizations.PureCircuitFinding
open Microsoft.Quantum.QsCompiler.SyntaxTree


type PreEvaluation =

    /// Attempts to pre-evaluate the given sequence of namespaces
    /// as much as possible with a script of optimizing transformations
    ///
    /// Some of the optimizing transformations need a dictionary of all
    /// callables by name.  Consequently, the script is generated by a
    /// function that takes as input such a dictionary of callables.
    static member Script (script : Callables -> OptimizingTransformation list) (arg : QsCompilation) =

        // TODO: this should actually only evaluate everything for each entry point
        let rec evaluate (tree : _ list) = 
            let mutable tree = tree
            tree <- List.map (StripAllKnownSymbols().Transform) tree
            tree <- List.map (VariableRenamer().Transform) tree

            let callables = GlobalCallableResolutions tree |> Callables // needs to be constructed in every iteration
            let optimizers = script callables
            for opt in optimizers do tree <- List.map opt.Transform tree
            if optimizers |> List.exists (fun opt -> opt.checkChanged()) then evaluate tree 
            else tree

        let namespaces = arg.Namespaces |> Seq.map StripPositionInfo.Apply |> List.ofSeq |> evaluate
        QsCompilation.New (namespaces.ToImmutableArray(), arg.EntryPoints)

    /// Default sequence of optimizing transformations
    static member private DefaultScript removeFunctions maxSize callables : OptimizingTransformation list =
        [
            VariableRemover()
            StatementRemover(removeFunctions)
            ConstantPropagator(callables)
            LoopUnroller(callables, maxSize)
            CallableInliner(callables)
            StatementReorderer()
            PureCircuitFinder(callables)
        ]

    /// Attempts to pre-evaluate the given sequence of namespaces
    /// as much as possible with a default optimization script
    static member All (arg : QsCompilation) =
        PreEvaluation.Script (PreEvaluation.DefaultScript false 40) arg