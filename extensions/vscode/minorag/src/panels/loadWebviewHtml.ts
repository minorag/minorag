import * as vscode from "vscode";
import * as path from "path";
import * as fs from "fs";

export function loadWebviewHtml(
  context: vscode.ExtensionContext,
  webview: vscode.Webview,
  relHtmlPath: string
): string {
  const diskPath = vscode.Uri.file(path.join(context.extensionPath, relHtmlPath));
  let html = fs.readFileSync(diskPath.fsPath, "utf8");

  // Replace placeholders with webview-safe URIs (recommended pattern)
  const mediaRoot = vscode.Uri.file(path.join(context.extensionPath, "media"));
  const mediaUri = webview.asWebviewUri(mediaRoot);

  html = html.replaceAll("{{MEDIA_URI}}", mediaUri.toString());
  html = html.replaceAll("{{CSP_SOURCE}}", webview.cspSource);
  html = html.replaceAll("{{NONCE}}", String(Date.now()));

  return html;
}