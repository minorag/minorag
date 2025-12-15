/******/ (() => { // webpackBootstrap
/******/ 	"use strict";
/******/ 	var __webpack_modules__ = ([
/* 0 */
/***/ (function(__unused_webpack_module, exports, __webpack_require__) {


var __createBinding = (this && this.__createBinding) || (Object.create ? (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    var desc = Object.getOwnPropertyDescriptor(m, k);
    if (!desc || ("get" in desc ? !m.__esModule : desc.writable || desc.configurable)) {
      desc = { enumerable: true, get: function() { return m[k]; } };
    }
    Object.defineProperty(o, k2, desc);
}) : (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    o[k2] = m[k];
}));
var __setModuleDefault = (this && this.__setModuleDefault) || (Object.create ? (function(o, v) {
    Object.defineProperty(o, "default", { enumerable: true, value: v });
}) : function(o, v) {
    o["default"] = v;
});
var __importStar = (this && this.__importStar) || (function () {
    var ownKeys = function(o) {
        ownKeys = Object.getOwnPropertyNames || function (o) {
            var ar = [];
            for (var k in o) if (Object.prototype.hasOwnProperty.call(o, k)) ar[ar.length] = k;
            return ar;
        };
        return ownKeys(o);
    };
    return function (mod) {
        if (mod && mod.__esModule) return mod;
        var result = {};
        if (mod != null) for (var k = ownKeys(mod), i = 0; i < k.length; i++) if (k[i] !== "default") __createBinding(result, mod, k[i]);
        __setModuleDefault(result, mod);
        return result;
    };
})();
Object.defineProperty(exports, "__esModule", ({ value: true }));
exports.activate = activate;
exports.deactivate = deactivate;
const vscode = __importStar(__webpack_require__(1));
const doctorPanel_1 = __webpack_require__(2);
const chatPanel_1 = __webpack_require__(5);
function getBaseUrl() {
    return vscode.workspace
        .getConfiguration("minorag")
        .get("apiBaseUrl", "http://localhost:9999");
}
function activate(context) {
    // ----------------------------
    // Doctor UI
    // ----------------------------
    context.subscriptions.push(vscode.commands.registerCommand("minorag.doctor", () => {
        doctorPanel_1.DoctorPanel.show(context);
        const baseUrl = getBaseUrl();
        doctorPanel_1.DoctorPanel.current?.postBaseUrl(baseUrl);
    }));
    context.subscriptions.push(vscode.commands.registerCommand("minorag._doctorInternal", async () => {
        const panel = doctorPanel_1.DoctorPanel.current;
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
                vscode.window.showErrorMessage(`Doctor failed: ${resp.status} ${text}`);
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
                let idx;
                while ((idx = buffer.indexOf("\n")) >= 0) {
                    const line = buffer.slice(0, idx).trim();
                    buffer = buffer.slice(idx + 1);
                    if (!line) {
                        continue;
                    }
                    try {
                        const row = JSON.parse(line);
                        panel.addRow(row);
                    }
                    catch {
                        // ignore malformed lines
                    }
                }
            }
            panel.setStatus("Done");
        }
        catch (err) {
            panel.setStatus("Failed");
            vscode.window.showErrorMessage(`Cannot reach Minorag.Api at ${baseUrl} (is it running?)\n${String(err?.message ?? err)}`);
        }
    }));
    // ----------------------------
    // Chat UI
    // ----------------------------
    context.subscriptions.push(vscode.commands.registerCommand("minorag.chat", () => {
        chatPanel_1.ChatPanel.show(context);
        const baseUrl = getBaseUrl();
        chatPanel_1.ChatPanel.current?.postBaseUrl(baseUrl);
        // Attach message handler once panel exists
        const panel = chatPanel_1.ChatPanel.current;
        if (!panel) {
            return;
        }
        panel.onMessage(async (msg) => {
            if (msg?.type === "ask") {
                await vscode.commands.executeCommand("minorag._chatInternal", msg);
            }
            else if (msg?.type === "loadRepos") {
                await vscode.commands.executeCommand("minorag._reposInternal", msg);
            }
        });
    }));
    context.subscriptions.push(vscode.commands.registerCommand("minorag._reposInternal", async () => {
        const panel = chatPanel_1.ChatPanel.current;
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
            const repos = await resp.json();
            panel.postRepos(repos);
        }
        catch (err) {
            panel.postStatus(`Cannot reach Minorag.Api at ${baseUrl} (is it running?)\n${String(err?.message ?? err)}`);
        }
    }));
    context.subscriptions.push(vscode.commands.registerCommand("minorag._chatInternal", async (msg) => {
        const panel = chatPanel_1.ChatPanel.current;
        if (!panel) {
            vscode.window.showErrorMessage("Chat panel is not open.");
            return;
        }
        const baseUrl = getBaseUrl();
        panel.postBaseUrl(baseUrl);
        const request = msg?.request;
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
        }
        catch (err) {
            panel.askError(answerId, `Cannot reach Minorag.Api at ${baseUrl} (is it running?)\n${String(err?.message ?? err)}`);
        }
    }));
}
function deactivate() { }


/***/ }),
/* 1 */
/***/ ((module) => {

module.exports = require("vscode");

/***/ }),
/* 2 */
/***/ (function(__unused_webpack_module, exports, __webpack_require__) {


var __createBinding = (this && this.__createBinding) || (Object.create ? (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    var desc = Object.getOwnPropertyDescriptor(m, k);
    if (!desc || ("get" in desc ? !m.__esModule : desc.writable || desc.configurable)) {
      desc = { enumerable: true, get: function() { return m[k]; } };
    }
    Object.defineProperty(o, k2, desc);
}) : (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    o[k2] = m[k];
}));
var __setModuleDefault = (this && this.__setModuleDefault) || (Object.create ? (function(o, v) {
    Object.defineProperty(o, "default", { enumerable: true, value: v });
}) : function(o, v) {
    o["default"] = v;
});
var __importStar = (this && this.__importStar) || (function () {
    var ownKeys = function(o) {
        ownKeys = Object.getOwnPropertyNames || function (o) {
            var ar = [];
            for (var k in o) if (Object.prototype.hasOwnProperty.call(o, k)) ar[ar.length] = k;
            return ar;
        };
        return ownKeys(o);
    };
    return function (mod) {
        if (mod && mod.__esModule) return mod;
        var result = {};
        if (mod != null) for (var k = ownKeys(mod), i = 0; i < k.length; i++) if (k[i] !== "default") __createBinding(result, mod, k[i]);
        __setModuleDefault(result, mod);
        return result;
    };
})();
Object.defineProperty(exports, "__esModule", ({ value: true }));
exports.DoctorPanel = void 0;
const vscode = __importStar(__webpack_require__(1));
class DoctorPanel {
    static current;
    panel;
    context;
    disposables = [];
    constructor(context, panel) {
        this.context = context;
        this.panel = panel;
        this.panel.webview.options = {
            enableScripts: true,
            localResourceRoots: [vscode.Uri.joinPath(context.extensionUri, "media")],
        };
        this.panel.webview.html = this.getHtml();
        this.panel.webview.onDidReceiveMessage(async (msg) => {
            if (!msg || !msg.type) {
                return;
            }
            if (msg.type === "runDoctor") {
                await vscode.commands.executeCommand("minorag._doctorInternal");
            }
        }, undefined, this.disposables);
        this.panel.onDidDispose(() => this.dispose(), null, this.disposables);
    }
    static show(context) {
        if (DoctorPanel.current) {
            DoctorPanel.current.panel.reveal(vscode.ViewColumn.Beside);
            return;
        }
        const panel = vscode.window.createWebviewPanel("minorag.doctor", "Minorag Doctor", vscode.ViewColumn.Beside, { enableScripts: true });
        DoctorPanel.current = new DoctorPanel(context, panel);
    }
    postBaseUrl(url) {
        this.panel.webview.postMessage({ type: "baseUrl", text: url });
    }
    clear() {
        this.panel.webview.postMessage({ type: "clear" });
    }
    setStatus(text) {
        this.panel.webview.postMessage({ type: "status", text });
    }
    done(text) {
        this.panel.webview.postMessage({ type: "done", text });
    }
    error(text) {
        this.panel.webview.postMessage({ type: "error", text });
    }
    addRow(row) {
        const ui = this.toUiRow(row);
        this.panel.webview.postMessage({ type: "row", row: ui });
    }
    toUiRow(r) {
        const sevMap = {
            0: "Warning",
            1: "Error",
            2: "Success",
            3: "Info",
        };
        const label = r.Label ?? "";
        const description = r.Description ?? "";
        const hint = r.Hint ?? null;
        const severity = sevMap[r.Severity ?? 3] ?? "Info";
        const isHeader = severity === "Info" &&
            label.length > 0 &&
            description.trim().length === 0;
        const isBlank = severity === "Info" &&
            label.trim().length === 0 &&
            description.trim().length === 0;
        return { label, description, hint, severity, isHeader, isBlank };
    }
    getHtml() {
        const webview = this.panel.webview;
        const nonce = String(Date.now());
        const htmlUri = vscode.Uri.joinPath(this.context.extensionUri, "media", "doctor.html");
        const htmlBytes = vscode.workspace.fs.readFile(htmlUri);
        // NOTE: this is async API, but VS Code expects html string sync.
        // So we do a small sync trick: use Buffer from fs read via thenable in constructor is not great.
        // Easiest: pre-read using Node fs because extension runs in Node.
        // We’ll implement it with require('fs') to keep it synchronous.
        const fs = __webpack_require__(3);
        const path = __webpack_require__(4);
        const diskPath = path.join(this.context.extensionPath, "media", "doctor.html");
        let html = fs.readFileSync(diskPath, "utf8");
        const scriptUri = webview.asWebviewUri(vscode.Uri.joinPath(this.context.extensionUri, "media", "doctor.js"));
        html = html.replaceAll("{{CSP_SOURCE}}", webview.cspSource);
        html = html.replaceAll("{{NONCE}}", nonce);
        html = html.replaceAll("{{SCRIPT_URI}}", scriptUri.toString());
        return html;
    }
    dispose() {
        DoctorPanel.current = undefined;
        while (this.disposables.length) {
            this.disposables.pop()?.dispose();
        }
    }
}
exports.DoctorPanel = DoctorPanel;


/***/ }),
/* 3 */
/***/ ((module) => {

module.exports = require("fs");

/***/ }),
/* 4 */
/***/ ((module) => {

module.exports = require("path");

/***/ }),
/* 5 */
/***/ (function(__unused_webpack_module, exports, __webpack_require__) {


var __createBinding = (this && this.__createBinding) || (Object.create ? (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    var desc = Object.getOwnPropertyDescriptor(m, k);
    if (!desc || ("get" in desc ? !m.__esModule : desc.writable || desc.configurable)) {
      desc = { enumerable: true, get: function() { return m[k]; } };
    }
    Object.defineProperty(o, k2, desc);
}) : (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    o[k2] = m[k];
}));
var __setModuleDefault = (this && this.__setModuleDefault) || (Object.create ? (function(o, v) {
    Object.defineProperty(o, "default", { enumerable: true, value: v });
}) : function(o, v) {
    o["default"] = v;
});
var __importStar = (this && this.__importStar) || (function () {
    var ownKeys = function(o) {
        ownKeys = Object.getOwnPropertyNames || function (o) {
            var ar = [];
            for (var k in o) if (Object.prototype.hasOwnProperty.call(o, k)) ar[ar.length] = k;
            return ar;
        };
        return ownKeys(o);
    };
    return function (mod) {
        if (mod && mod.__esModule) return mod;
        var result = {};
        if (mod != null) for (var k = ownKeys(mod), i = 0; i < k.length; i++) if (k[i] !== "default") __createBinding(result, mod, k[i]);
        __setModuleDefault(result, mod);
        return result;
    };
})();
Object.defineProperty(exports, "__esModule", ({ value: true }));
exports.ChatPanel = void 0;
const vscode = __importStar(__webpack_require__(1));
class ChatPanel {
    static current;
    panel;
    context;
    disposables = [];
    constructor(context, panel) {
        this.context = context;
        this.panel = panel;
        this.panel.webview.options = {
            enableScripts: true,
            localResourceRoots: [vscode.Uri.joinPath(context.extensionUri, "media")],
        };
        this.panel.webview.html = this.getHtml();
        this.panel.onDidDispose(() => this.dispose(), null, this.disposables);
    }
    static show(context) {
        if (ChatPanel.current) {
            ChatPanel.current.panel.reveal(vscode.ViewColumn.Beside);
            return;
        }
        const panel = vscode.window.createWebviewPanel("minorag.chat", "Minorag Chat", vscode.ViewColumn.Beside, {
            enableScripts: true,
            retainContextWhenHidden: true, // ✅ keep DOM/JS alive when switching tabs
        });
        ChatPanel.current = new ChatPanel(context, panel);
    }
    onMessage(handler) {
        this.panel.webview.onDidReceiveMessage(handler, undefined, this.disposables);
    }
    postBaseUrl(url) {
        this.panel.webview.postMessage({ type: "baseUrl", text: url });
    }
    postStatus(text) {
        this.panel.webview.postMessage({ type: "status", text });
    }
    postRepos(repos) {
        this.panel.webview.postMessage({ type: "repos", repos });
    }
    askStart(answerId) {
        this.panel.webview.postMessage({ type: "askStart", answerId });
    }
    askChunk(answerId, text) {
        this.panel.webview.postMessage({ type: "askChunk", answerId, text });
    }
    askDone(answerId) {
        this.panel.webview.postMessage({ type: "askDone", answerId });
    }
    askError(answerId, text) {
        this.panel.webview.postMessage({ type: "askError", answerId, text });
    }
    getHtml() {
        const webview = this.panel.webview;
        const nonce = String(Date.now());
        const fs = __webpack_require__(3);
        const path = __webpack_require__(4);
        const diskPath = path.join(this.context.extensionPath, "media", "chat.html");
        let html = fs.readFileSync(diskPath, "utf8");
        const scriptUri = webview.asWebviewUri(vscode.Uri.joinPath(this.context.extensionUri, "media", "chat.js"));
        const prismJsUri = webview.asWebviewUri(vscode.Uri.joinPath(this.context.extensionUri, "media", "prism", "prism.js"));
        const prismCssUri = webview.asWebviewUri(vscode.Uri.joinPath(this.context.extensionUri, "media", "prism", "prism.css"));
        html = html.replaceAll("{{PRISM_JS_URI}}", prismJsUri.toString());
        html = html.replaceAll("{{PRISM_CSS_URI}}", prismCssUri.toString());
        html = html.replaceAll("{{CSP_SOURCE}}", webview.cspSource);
        html = html.replaceAll("{{NONCE}}", nonce);
        html = html.replaceAll("{{SCRIPT_URI}}", scriptUri.toString());
        return html;
    }
    dispose() {
        ChatPanel.current = undefined;
        while (this.disposables.length) {
            this.disposables.pop()?.dispose();
        }
    }
}
exports.ChatPanel = ChatPanel;


/***/ })
/******/ 	]);
/************************************************************************/
/******/ 	// The module cache
/******/ 	var __webpack_module_cache__ = {};
/******/ 	
/******/ 	// The require function
/******/ 	function __webpack_require__(moduleId) {
/******/ 		// Check if module is in cache
/******/ 		var cachedModule = __webpack_module_cache__[moduleId];
/******/ 		if (cachedModule !== undefined) {
/******/ 			return cachedModule.exports;
/******/ 		}
/******/ 		// Create a new module (and put it into the cache)
/******/ 		var module = __webpack_module_cache__[moduleId] = {
/******/ 			// no module.id needed
/******/ 			// no module.loaded needed
/******/ 			exports: {}
/******/ 		};
/******/ 	
/******/ 		// Execute the module function
/******/ 		__webpack_modules__[moduleId].call(module.exports, module, module.exports, __webpack_require__);
/******/ 	
/******/ 		// Return the exports of the module
/******/ 		return module.exports;
/******/ 	}
/******/ 	
/************************************************************************/
/******/ 	
/******/ 	// startup
/******/ 	// Load entry module and return exports
/******/ 	// This entry module is referenced by other modules so it can't be inlined
/******/ 	var __webpack_exports__ = __webpack_require__(0);
/******/ 	module.exports = __webpack_exports__;
/******/ 	
/******/ })()
;
//# sourceMappingURL=extension.js.map