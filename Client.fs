namespace CompileJSLive

open WebSharper
open WebSharper.UI
open WebSharper.UI.Templating
open WebSharper.UI.Notation
open WebSharper.UI.Html
open WebSharper.UI.Client

[<JavaScript>]
module Templates =

    type MainTemplate = Templating.Template<"Main.html", ClientLoad.FromDocument, ServerLoad.WhenChanged>

[<JavaScript>]
module Client =

    let IsFSharp = Var.Create(true)    

    let Main () =
        let fSharpSourceCode = Var.Create """module MyModule

open WebSharper 
open WebSharper.JavaScript

[<JavaScript>]
let Main () = 
    Console.Log "Hello world!"
"""

        let fSharpResponse = Var.Create ""
        
        let cSharpSourceCode = Var.Create """using WebSharper;
using WebSharper.JavaScript;

[JavaScript]
public static class Main {
    public static void Run() { 
        Console.Log("Hello world!");
    }
}
"""

        let cSharpResponse = Var.Create ""

        Doc.Concat [
            Templates.MainTemplate.MainForm()
                .Attrs(attr.``class`` (if IsFSharp.V then "row" else "hidden"))
                .Editor(Doc.InputType.TextArea [attr.spellcheck "false"] fSharpSourceCode)
                .OnSend(fun e ->
                    async {
                        fSharpResponse := "Compiling..." 
                        let! res = Server.TranslateFSharp fSharpSourceCode.Value
                        fSharpResponse := res
                    }
                    |> Async.StartImmediate
                )
                .Response(fSharpResponse.View)
                .Doc()
        
            Templates.MainTemplate.MainForm()
                .Attrs(attr.``class`` (if IsFSharp.V then "hidden" else "row"))
                .Editor(Doc.InputType.TextArea [attr.spellcheck "false"] cSharpSourceCode)
                .OnSend(fun e ->
                    async {
                        cSharpResponse := "Compiling..."
                        let! res = Server.TranslateCSharp cSharpSourceCode.Value
                        cSharpResponse := res
                    }
                    |> Async.StartImmediate
                )
                .Response(cSharpResponse.View)
                .Doc()
        ]

    // Compute a menubar where the menu item for the given endpoint is active
    let MenuBar () =
        let ( => ) txt isFSharp =
             li [attr.``class`` (if IsFSharp.V = isFSharp then "active" else "")] [
                a [
                    attr.href "#"
                    on.click (fun _ _ -> IsFSharp.Value <- isFSharp)
                ] [text txt]
             ]
        Doc.Concat [
            "From F#" => true
            "From C#" => false
        ]
