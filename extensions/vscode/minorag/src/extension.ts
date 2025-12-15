import * as vscode from "vscode";
import { DoctorPanel } from "./panels/doctorPanel";
import { ChatPanel } from "./panels/chatPanel";

type AskRequest = {
  currentDirectory: string | null;
  question: string;
  topK: number;
  explicitRepoNames: string[];
  reposCsv: string | null;
  projectName: string | null;
  clientName: string | null;
  noLlm: boolean;
  verbose: boolean;
  allRepos: boolean;
  useAdvancedModel: boolean;
};

function getBaseUrl(): string {
  return vscode.workspace
    .getConfiguration("minorag")
    .get<string>("apiBaseUrl", "http://localhost:9999");
}

export function activate(context: vscode.ExtensionContext) {
  // ----------------------------
  // Doctor UI
  // ----------------------------
  context.subscriptions.push(
    vscode.commands.registerCommand("minorag.doctor", () => {
      DoctorPanel.show(context);

      const baseUrl = getBaseUrl();
      DoctorPanel.current?.postBaseUrl(baseUrl);
    })
  );

  context.subscriptions.push(
    vscode.commands.registerCommand("minorag._doctorInternal", async () => {
      const panel = DoctorPanel.current;
      if (!panel) {
        vscode.window.showErrorMessage("Doctor panel is not open.");
        return;
      }

      const baseUrl = getBaseUrl();
      panel.postBaseUrl(baseUrl);

      panel.clear();
      panel.setStatus("Running…");

      try {
        const resp = await fetch(`${baseUrl}/doctor`, {
          method: "PATCH",
          headers: { Accept: "application/x-ndjson" },
        });

        if (!resp.ok || !resp.body) {
          const text = await resp.text().catch(() => "");
          panel.setStatus("Failed");
          vscode.window.showErrorMessage(
            `Doctor failed: ${resp.status} ${text}`
          );
          return;
        }

        const reader = resp.body.getReader();
        const decoder = new TextDecoder("utf-8");
        let buffer = "";

        while (true) {
          const { value, done } = await reader.read();
          if (done) {
            break;
          }

          buffer += decoder.decode(value, { stream: true });

          let idx: number;
          while ((idx = buffer.indexOf("\n")) >= 0) {
            const line = buffer.slice(0, idx).trim();
            buffer = buffer.slice(idx + 1);

            if (!line) {
              continue;
            }

            try {
              const row = JSON.parse(line);
              panel.addRow(row);
            } catch {
              // ignore malformed lines
            }
          }
        }

        panel.setStatus("Done");
      } catch (err: any) {
        panel.setStatus("Failed");
        vscode.window.showErrorMessage(
          `Cannot reach Minorag.Api at ${baseUrl} (is it running?)\n${String(
            err?.message ?? err
          )}`
        );
      }
    })
  );

  // ----------------------------
  // Chat UI
  // ----------------------------
  context.subscriptions.push(
    vscode.commands.registerCommand("minorag.chat", () => {
      ChatPanel.show(context);

      const baseUrl = getBaseUrl();
      ChatPanel.current?.postBaseUrl(baseUrl);

      // Attach message handler once panel exists
      const panel = ChatPanel.current;
      if (!panel) {
        return;
      }

      panel.onMessage(async (msg) => {
        if (msg?.type === "ask") {
          await vscode.commands.executeCommand("minorag._chatInternal", msg);
        } else if (msg?.type === "loadRepos") {
          await vscode.commands.executeCommand("minorag._reposInternal", msg);
        }
      });
    })
  );

  context.subscriptions.push(
    vscode.commands.registerCommand("minorag._reposInternal", async () => {
      const panel = ChatPanel.current;
      if (!panel) {
        return;
      }

      const baseUrl = getBaseUrl();
      panel.postBaseUrl(baseUrl);

      try {
        const resp = await fetch(`${baseUrl}/repos`, {
          method: "GET",
          headers: { Accept: "application/json" },
        });

        if (!resp.ok) {
          const text = await resp.text().catch(() => "");
          panel.postStatus(`Failed to load repos: ${resp.status} ${text}`);
          return;
        }

        const repos: any = await resp.json();
        panel.postRepos(repos);
      } catch (err: any) {
        panel.postStatus(
          `Cannot reach Minorag.Api at ${baseUrl} (is it running?)\n${String(
            err?.message ?? err
          )}`
        );
      }
    })
  );

  context.subscriptions.push(
    vscode.commands.registerCommand(
      "minorag._chatInternal",
      async (msg: any) => {
        const panel = ChatPanel.current;
        if (!panel) {
          vscode.window.showErrorMessage("Chat panel is not open.");
          return;
        }

        const baseUrl = getBaseUrl();
        panel.postBaseUrl(baseUrl);

        const request = msg?.request as AskRequest | undefined;
        const answerId = String(msg?.answerId ?? "");

        if (!request || !request.question || !answerId) {
          panel.askError(answerId, "Invalid request from webview.");
          return;
        }

        try {
          panel.askStart(answerId);

          const resp = await fetch(`${baseUrl}/ask`, {
            method: "POST",
            headers: {
              "Content-Type": "application/json",
              Accept: "*/*",
            },
            body: JSON.stringify(request),
          });

          if (!resp.ok) {
            const text = await resp.text().catch(() => "");
            panel.askError(answerId, `Ask failed: ${resp.status} ${text}`);
            return;
          }

          // Two possible API behaviors:
          // 1) text/plain streamed (your current behavior when useLlm && hasResults)
          // 2) JSON SearchResult (when noLlm || !hasResults)
          const contentType = resp.headers.get("content-type") ?? "";

          if (contentType.includes("application/json")) {
            const json = await resp.json();
            const pretty = JSON.stringify(json, null, 2);
            panel.askChunk(answerId, pretty);
            panel.askDone(answerId);
            return;
          }

          if (!resp.body) {
            const text = await resp.text().catch(() => "");
            panel.askChunk(answerId, text);
            panel.askDone(answerId);
            return;
          }

          // Stream text/plain chunks
          const reader = resp.body.getReader();
          const decoder = new TextDecoder("utf-8");

          let accumulated = "";

          while (true) {
            const { value, done } = await reader.read();
            if (done) {
              break;
            }

            const chunk = decoder.decode(value, { stream: true });
            if (!chunk) {
              continue;
            }

            accumulated += chunk;
            panel.askChunk(answerId, chunk);
          }

          panel.askDone(answerId);
        } catch (err: any) {
          panel.askError(
            answerId,
            `Cannot reach Minorag.Api at ${baseUrl} (is it running?)\n${String(
              err?.message ?? err
            )}`
          );
        }
      }
    )
  );
}

export function deactivate() {}
