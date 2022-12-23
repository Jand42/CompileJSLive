namespace CompileJSLive

open WebSharper
open WebSharper.Sitelets
open WebSharper.UI
open WebSharper.UI.Server

module Site =
    open WebSharper.UI.Html

    open type WebSharper.UI.ClientServer

    let HomePage ctx =
        Content.Page(
            Templates.MainTemplate()
                .Title("Compile to JS with WebSharper")
                .MenuBar(client (Client.MenuBar()))
                .Body(client (Client.Main()))
                .Doc()
        )

    [<Website>]
    let Main =
        Application.SinglePage (fun ctx ->
            HomePage ctx
        )
