import * as vscode from "vscode";

export class ChatPanel {
  public static current: ChatPanel | undefined;

  private readonly panel: vscode.WebviewPanel;
  private readonly context: vscode.ExtensionContext;
  private readonly disposables: vscode.Disposable[] = [];

  private constructor(context: vscode.ExtensionContext, panel: vscode.WebviewPanel) {
    this.context = context;
    this.panel = panel;

    this.panel.webview.options = {
      enableScripts: true,
      localResourceRoots: [vscode.Uri.joinPath(context.extensionUri, "media")],
    };

    this.panel.webview.html = this.getHtml();
    this.panel.onDidDispose(() => this.dispose(), null, this.disposables);
  }

  static show(context: vscode.ExtensionContext) {
    if (ChatPanel.current) {
      ChatPanel.current.panel.reveal(vscode.ViewColumn.Beside);
      return;
    }

    const panel = vscode.window.createWebviewPanel(
      "minorag.chat",
      "Minorag Chat",
      vscode.ViewColumn.Beside,
      {
        enableScripts: true,
        retainContextWhenHidden: true, // ✅ keep DOM/JS alive when switching tabs
      }
    );

    ChatPanel.current = new ChatPanel(context, panel);
  }

  onMessage(handler: (msg: any) => void) {
    this.panel.webview.onDidReceiveMessage(handler, undefined, this.disposables);
  }

  postBaseUrl(url: string) {
    this.panel.webview.postMessage({ type: "baseUrl", text: url });
  }

  postStatus(text: string) {
    this.panel.webview.postMessage({ type: "status", text });
  }

  postRepos(repos: any[]) {
    this.panel.webview.postMessage({ type: "repos", repos });
  }

  askStart(answerId: string) {
    this.panel.webview.postMessage({ type: "askStart", answerId });
  }

  askChunk(answerId: string, text: string) {
    this.panel.webview.postMessage({ type: "askChunk", answerId, text });
  }

  askDone(answerId: string) {
    this.panel.webview.postMessage({ type: "askDone", answerId });
  }

  askError(answerId: string, text: string) {
    this.panel.webview.postMessage({ type: "askError", answerId, text });
  }

  private getHtml(): string {
    const webview = this.panel.webview;
    const nonce = String(Date.now());

    const fs = require("fs") as typeof import("fs");
    const path = require("path") as typeof import("path");

    const diskPath = path.join(this.context.extensionPath, "media", "chat.html");
    let html = fs.readFileSync(diskPath, "utf8");

    const scriptUri = webview.asWebviewUri(
      vscode.Uri.joinPath(this.context.extensionUri, "media", "chat.js")
    );

    const prismJsUri = webview.asWebviewUri(
      vscode.Uri.joinPath(this.context.extensionUri, "media", "prism", "prism.js")
    );
    const prismCssUri = webview.asWebviewUri(
      vscode.Uri.joinPath(this.context.extensionUri, "media", "prism", "prism.css")
    );

    html = html.replaceAll("{{PRISM_JS_URI}}", prismJsUri.toString());
    html = html.replaceAll("{{PRISM_CSS_URI}}", prismCssUri.toString());

    html = html.replaceAll("{{CSP_SOURCE}}", webview.cspSource);
    html = html.replaceAll("{{NONCE}}", nonce);
    html = html.replaceAll("{{SCRIPT_URI}}", scriptUri.toString());

    return html;
  }

  private dispose() {
    ChatPanel.current = undefined;
    while (this.disposables.length) {
      this.disposables.pop()?.dispose();
    }
  }
}