namespace CompileJSLive

open WebSharper

open System.IO

open System
open System.IO
open System.Collections.Generic
open FSharp.Compiler
open FSharp.Compiler.Symbols
open FSharp.Compiler.CodeAnalysis
open Microsoft.CodeAnalysis
open Microsoft.CodeAnalysis.CSharp

// do not log compiler messages
type EmptyLogger() =
    inherit WebSharper.Compiler.LoggerBase()

    override this.Error _ = ()
    override this.Out _ = ()

module Server =

    // Create an interactive checker instance 
    let checker = FSharpChecker.Create(keepAssemblyContents=true)

    let wsRefs =
        let getAssemblyPath (name: string) =
            System.Reflection.Assembly.Load(name).Location
        List.map getAssemblyPath [
            "WebSharper.Core.JavaScript"
            "WebSharper.Core"
            "WebSharper.JavaScript"
            "WebSharper.Main"
            "WebSharper.Collections"
            "WebSharper.Control"
            "WebSharper.Web"
            "WebSharper.Sitelets"
        ]

    let mkProjectCommandLineArgs (dllName, fileNames) = 
        [|  
            yield "--simpleresolution" 
            yield "--debug:full" 
            yield "--define:DEBUG" 
            yield "--optimize-" 
            yield "--out:" + dllName
            yield "--doc:test.xml" 
            yield "--warn:3" 
            yield "--fullpaths" 
            yield "--flaterrors" 
            yield "--target:library" 
            for x in fileNames do 
                yield x
            for r in wsRefs do
                yield "-r:" + r
         |]
    
    [<Rpc>]
    let TranslateFSharp source = 

        // If you are not working from inside an RPC, you can also get WebSharper metadata from:
        //   * from ASP.NET Core dependency injection service method IWebSharperService.GetWebSharperMeta  
        //   * WebSharper.Core.Metadata.IO.LoadRuntimeMetadata from a single WebSharper web project that has pre-merged metadata for runtime
        //   * WebSharper.Compiler.FrontEnd.TryReadFromAssembly on individual assemblies then WebSharper.Core.Metadata.Info.UnionWithoutDependencies to merge them 

        let ctx = WebSharper.Web.Remoting.GetContext()
        let metadata = ctx.Metadata
        
        // preparing F# source file, we could also use in-memory file system here but this is simpler 
        let tempFileName = Path.GetTempFileName()
        let fileName = Path.ChangeExtension(tempFileName, ".fs")
        let base2 = Path.GetTempFileName()
        let dllName = Path.ChangeExtension(base2, ".dll")
        let projFileName = Path.ChangeExtension(base2, ".fsproj")
        File.WriteAllText(fileName, source)

        let args = mkProjectCommandLineArgs (dllName, [fileName])
        let options = checker.GetProjectOptionsFromCommandLineArgs (projFileName, args)

        async {
            let! wholeProjectResults = checker.ParseAndCheckProject(options)
            
            if wholeProjectResults.Diagnostics |> Seq.exists (fun err -> err.Severity = Diagnostics.FSharpDiagnosticSeverity.Error) then
                return 
                    wholeProjectResults.Diagnostics |> Seq.filter (fun err -> err.Severity = Diagnostics.FSharpDiagnosticSeverity.Error) 
                    |> Seq.map (fun err -> sprintf "F# Error: %d:%d-%d:%d %s" err.StartLine err.StartColumn err.EndLine err.EndColumn err.Message)
                    |> String.concat Environment.NewLine
                    
            else

                let logger = EmptyLogger()

                // Create the WebSharper compilation object
                let comp = 
                    WebSharper.Compiler.FSharp.ProjectReader.transformAssembly
                        logger
                        (WebSharper.Compiler.Compilation(metadata, false, UseLocalMacros = false))
                        "TestProject"
                        WebSharper.Compiler.CommandTools.WsConfig.Empty
                        wholeProjectResults

                // Do full compilation
                WebSharper.Compiler.Translator.DotNetToJavaScript.CompileFull comp

                if not (List.isEmpty comp.Errors) then
                    return
                        comp.Errors
                        |> Seq.map (fun (pos, err) -> sprintf "WebSharper Error: %A %O" pos err)
                        |> String.concat Environment.NewLine
                     
                else

                    let currentMeta = comp.ToCurrentMetadata()

                    let pkg = WebSharper.Compiler.Packager.packageAssembly metadata currentMeta None WebSharper.Compiler.Packager.OnLoadIfExists
    
                    let js, jsMap = pkg |> WebSharper.Compiler.Packager.exprToString WebSharper.Core.JavaScript.Readable WebSharper.Core.JavaScript.Writer.CodeWriter                                       

                    return js
        }
  
    let csharpRefs = 
        List.concat [
            [
                typeof<obj> // System.Private.CoreLib
                typeof<unit> // FSharp.Core
            ]
            |> List.map (fun t ->
                let l = t.Assembly.Location
                printfn "ref: %s"  l
                MetadataReference.CreateFromFile(l) :> MetadataReference
            )
    
            let appFolderLocation = Path.GetDirectoryName (typeof<int>.Assembly.Location)

            [
                "netstandard.dll"
                "System.Runtime.dll"
            ]
            |> List.map (fun a ->
                let l = Path.Combine(appFolderLocation, a) 
                MetadataReference.CreateFromFile(l) :> MetadataReference
            )

            wsRefs
            |> List.map (fun r ->
                MetadataReference.CreateFromFile(r) :> MetadataReference
            )
        ]
    
    [<Rpc>]
    let TranslateCSharp (source: string) = 

        let ctx = WebSharper.Web.Remoting.GetContext()
        let metadata = ctx.Metadata

        let parseOptions = CSharpParseOptions(kind = SourceCodeKind.Script)

        let syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions)
    
        let csharpCompilation =
            CSharpCompilation.CreateScriptCompilation("Script", syntaxTree, csharpRefs)

        let diag = csharpCompilation.GetDiagnostics()

        if diag |> Seq.exists (fun d -> d.Severity = DiagnosticSeverity.Error) then
            diag |> Seq.filter (fun err -> err.Severity = DiagnosticSeverity.Error) 
            |> Seq.map (fun err -> sprintf "C# Error: %A" err)
            |> String.concat Environment.NewLine
                    
        else

            // Create the WebSharper compilation object
            let comp = 
                WebSharper.Compiler.CSharp.ProjectReader.transformAssembly
                    (WebSharper.Compiler.Compilation(metadata, false, UseLocalMacros = false))
                    WebSharper.Compiler.CommandTools.WsConfig.Empty
                    csharpCompilation

            // Do full compilation
            WebSharper.Compiler.Translator.DotNetToJavaScript.CompileFull comp

            if not (List.isEmpty comp.Errors) then
                comp.Errors
                |> Seq.map (fun (pos, err) -> sprintf "WebSharper Error: %A %O" pos err)
                |> String.concat Environment.NewLine
            else

                let currentMeta = comp.ToCurrentMetadata()
        
                let pkg = WebSharper.Compiler.Packager.packageAssembly metadata currentMeta None WebSharper.Compiler.Packager.OnLoadIfExists
    
                let js, map = pkg |> WebSharper.Compiler.Packager.exprToString WebSharper.Core.JavaScript.Readable WebSharper.Core.JavaScript.Writer.CodeWriter                                       

                js
        
        |> async.Return

