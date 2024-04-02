module ope.Client.Main

open System.IO
open System.Net.Http
open System.Reflection
open System.Text
open Elmish
open Bolero
open Bolero.Html
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Interactive.Shell
open FSharp.Compiler.Text
let dll_list =
    [
            "mscorlib"
            "FSharp.Core"
            "System"
            "System.Xml"
            "System.Runtime.Remoting"
            "System.Runtime.Serialization.Formatters.Soap"
            "System.Data"
            "System.Drawing"
            "System.Core"
            "System.Private.CoreLib"
            "System.Configuration"

            // These are the Portable-profile and .NET Standard 1.6 dependencies of FSharp.Core.dll.  These are needed
            // when an F# script references an F# profile 7, 78, 259 or .NET Standard 1.6 component which in turn refers
            // to FSharp.Core for profile 7, 78, 259 or .NET Standard.
            "netstandard"
            "System.Runtime" // lots of types
            "System.Linq" // System.Linq.Expressions.Expression<T>
            "System.Reflection" // System.Reflection.ParameterInfo
            "System.Linq.Expressions" // System.Linq.IQueryable<T>
            "System.Threading.Tasks" // valuetype [System.Threading.Tasks]System.Threading.CancellationToken
            "System.IO" //  System.IO.TextWriter
            "System.Net.WebClient"
            "System.Net.Requests" //  System.Net.WebResponse etc.
            "System.Collections" // System.Collections.Generic.List<T>
            "System.Runtime.Numerics" // BigInteger
            "System.Threading" // OperationCanceledException
            "System.Web"
            "System.Web.Services"
            "System.Windows.Forms"
            "System.Numerics"
    ]
    
let setup (client: HttpClient) name = task {
    let! runtime = client.GetByteArrayAsync("http://localhost:5083/dll/" + name)
    return runtime
}
let sampleCode = "\
for i in 1..10 do
    printfn $\"hello world {i}\"
"
let initTempDir () = task {
    let client = new HttpClient()
    let dllsToWrite = dll_list |> List.map (fun a -> a + ".dll")
    for dll in dllsToWrite do
        try
            printfn "DLL: %s" dll
            printfn "reading bytes from http"
            let! bytes = setup client dll
            printfn "got bytes"
            File.WriteAllBytes("/tmp/" + dll, bytes)
            printfn "wrote bytes"
            printfn "%A" bytes
        with error -> printfn $"{error}"
}
    
let compileCode (checker: FSharpChecker) (code: string) = task {
    let fn = Path.GetTempFileName()
    let fn2 = Path.ChangeExtension(fn, ".fsx")

    File.WriteAllText(fn2, code)
    try
        let! result = checker.GetProjectOptionsFromScript(fn2, SourceText.ofString (File.ReadAllText fn2), assumeDotNetFramework=false)
        printfn "%A" result
        // let! foo = checker.ParseAndCheckProject (fst result)
        // printfn "%A" foo
        // printfn "%A" foo.AssemblyContents.ImplementationFiles
        // for file in foo.AssemblyContents.ImplementationFiles do
        //     printfn $"File: %A{file}"
        //     for decl in file.Declarations do
        //         printfn "%A" decl
                
        let compilerOpts = [|
            "fsc.exe"
            yield! (fst result).OtherOptions
            "-a"
            fn2
            // "--target:exe"
            "-o:/tmp/tmp.dll"
        |]
        let! diagnostics, code = checker.Compile(compilerOpts)
        printfn "code = %A\n%A" code diagnostics
        let outputBytes = File.ReadAllBytes "/tmp/tmp.dll"
        // printfn "output bytes = %A" outputBytes
        let asm = Assembly.Load outputBytes
        asm.DefinedTypes |> Seq.tryFind (fun t -> t.FullName.Contains "StartupCode") |> Option.iter (fun startup ->
            startup.DeclaredConstructors |> Seq.head |> _.Invoke(null, [||])
            |> printfn "%A")
        
        // let fsi = FsiEvaluationSession.Create (FsiEvaluationSession.GetDefaultConfiguration(), [| "/fsc.dll" |], StringReader "", StringWriter(StringBuilder()), StringWriter(StringBuilder()))
        // fsi.EvalExpression "40 + 2" |> _.Value |> printfn "%A"
    with error ->
        printfn $"{error}"
}
type App() =
    inherit Component()
    let mutable textInput = sampleCode
    let mutable checker = Unchecked.defaultof<_>
    override this.OnAfterRenderAsync first =
        if first then
            task {
                do! initTempDir ()
                checker <- FSharpChecker.Create(keepAssemblyContents=true)
            }
        else
            task { () }
    override this.Render () =
        div {
            div {
                button {
                    on.click (fun _ -> compileCode checker textInput |> ignore)
                    text "Compile"
                }
            }
            textarea {
                attr.value textInput
                attr.style "width: 50vw; height: 90vh"
                on.input (fun e -> textInput <- string e.Value)
            }
        }
