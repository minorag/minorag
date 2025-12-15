import * as vscode from "vscode";

type ApiDoctorRow = {
  Label?: string;
  Description?: string;
  Severity?: number;
  Hint?: string | null;
};

type UiDoctorRow = {
  label: string;
  description: string;
  severity: "Error" | "Warning" | "Success" | "Info";
  hint?: string | null;
  isHeader?: boolean;
  isBlank?: boolean;
};

export class DoctorPanel {
  public static current: DoctorPanel | undefined;

  private readonly panel: vscode.WebviewPanel;
  private readonly context: vscode.ExtensionContext;
  private readonly disposables: vscode.Disposable[] = [];

  private constructor(
    context: vscode.ExtensionContext,
    panel: vscode.WebviewPanel
  ) {
    this.context = context;
    this.panel = panel;

    this.panel.webview.options = {
      enableScripts: true,
      localResourceRoots: [vscode.Uri.joinPath(context.extensionUri, "media")],
    };

    this.panel.webview.html = this.getHtml();

    this.panel.webview.onDidReceiveMessage(
      async (msg) => {
        if (!msg || !msg.type) {
          return;
        }

        if (msg.type === "runDoctor") {
          await vscode.commands.executeCommand("minorag._doctorInternal");
        }
      },
      undefined,
      this.disposables
    );

    this.panel.onDidDispose(() => this.dispose(), null, this.disposables);
  }

  static show(context: vscode.ExtensionContext) {
    if (DoctorPanel.current) {
      DoctorPanel.current.panel.reveal(vscode.ViewColumn.Beside);
      return;
    }

    const panel = vscode.window.createWebviewPanel(
      "minorag.doctor",
      "Minorag Doctor",
      vscode.ViewColumn.Beside,
      { enableScripts: true }
    );

    DoctorPanel.current = new DoctorPanel(context, panel);
  }

  postBaseUrl(url: string) {
    this.panel.webview.postMessage({ type: "baseUrl", text: url });
  }

  clear() {
    this.panel.webview.postMessage({ type: "clear" });
  }

  setStatus(text: string) {
    this.panel.webview.postMessage({ type: "status", text });
  }

  done(text: string) {
    this.panel.webview.postMessage({ type: "done", text });
  }

  error(text: string) {
    this.panel.webview.postMessage({ type: "error", text });
  }

  addRow(row: ApiDoctorRow) {
    const ui = this.toUiRow(row);
    this.panel.webview.postMessage({ type: "row", row: ui });
  }

  private toUiRow(r: ApiDoctorRow): UiDoctorRow {
    const sevMap: Record<number, UiDoctorRow["severity"]> = {
      0: "Warning",
      1: "Error",
      2: "Success",
      3: "Info",
    };

    const label = r.Label ?? "";
    const description = r.Description ?? "";
    const hint = r.Hint ?? null;

    const severity = sevMap[r.Severity ?? 3] ?? "Info";

    const isHeader =
      severity === "Info" &&
      label.length > 0 &&
      description.trim().length === 0;
    const isBlank =
      severity === "Info" &&
      label.trim().length === 0 &&
      description.trim().length === 0;

    return { label, description, hint, severity, isHeader, isBlank };
  }

  private getHtml(): string {
    const webview = this.panel.webview;

    const nonce = String(Date.now());

    const htmlUri = vscode.Uri.joinPath(
      this.context.extensionUri,
      "media",
      "doctor.html"
    );
    const htmlBytes = vscode.workspace.fs.readFile(htmlUri);

    // NOTE: this is async API, but VS Code expects html string sync.
    // So we do a small sync trick: use Buffer from fs read via thenable in constructor is not great.
    // Easiest: pre-read using Node fs because extension runs in Node.
    // We’ll implement it with require('fs') to keep it synchronous.

    const fs = require("fs") as typeof import("fs");
    const path = require("path") as typeof import("path");

    const diskPath = path.join(
      this.context.extensionPath,
      "media",
      "doctor.html"
    );
    let html = fs.readFileSync(diskPath, "utf8");

    const scriptUri = webview.asWebviewUri(
      vscode.Uri.joinPath(this.context.extensionUri, "media", "doctor.js")
    );

    html = html.replaceAll("{{CSP_SOURCE}}", webview.cspSource);
    html = html.replaceAll("{{NONCE}}", nonce);
    html = html.replaceAll("{{SCRIPT_URI}}", scriptUri.toString());

    return html;
  }

  private dispose() {
    DoctorPanel.current = undefined;
    while (this.disposables.length) {
      this.disposables.pop()?.dispose();
    }
  }
}
