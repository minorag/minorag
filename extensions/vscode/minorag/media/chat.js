(function () {
    const vscode = acquireVsCodeApi();

    const log = document.getElementById("log");
    const statusEl = document.getElementById("status");
    const baseUrlEl = document.getElementById("baseUrl");

    const questionEl = document.getElementById("question");
    const sendBtn = document.getElementById("send");
    const clearBtn = document.getElementById("clear");

    const noLlmEl = document.getElementById("noLlm");
    const verboseEl = document.getElementById("verbose");
    const allReposEl = document.getElementById("allRepos");
    const useAdvancedModelEl = document.getElementById("useAdvancedModel");
    const topKEl = document.getElementById("topK");
    const reposCsvEl = document.getElementById("reposCsv");
    const explicitRepoEl = document.getElementById("explicitRepo");

    const answerBuffers = new Map(); // answerId -> string

    function now() {
        const d = new Date();
        return d.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit" });
    }

    function setStatus(t) { statusEl.textContent = t ?? ""; }
    function setBaseUrl(t) { baseUrlEl.textContent = t ?? "—"; }

    function escapeHtml(s) {
        return String(s).replace(/[&<>"']/g, (c) => ({
            "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;",
        }[c]));
    }

    // --- Markdown (minimal, safe) + pipe tables + code fences (with language-xxx for Prism)
    function renderMarkdown(md) {
        const src = String(md ?? "");
        const esc = escapeHtml(src);

        function isTableSep(line) {
            const t = line.trim();
            if (!t.includes("-")) {
                return false;
            }
            const s = t.replace(/^\|/, "").replace(/\|$/, "");
            const cells = s.split("|").map(c => c.trim());
            return cells.length >= 2 && cells.every(c => /^:?-{3,}:?$/.test(c));
        }

        function splitRow(line) {
            const s = line.trim().replace(/^\|/, "").replace(/\|$/, "");
            return s.split("|").map(c => c.trim());
        }

        function renderTable(lines) {
            const header = splitRow(lines[0]);
            const alignLine = splitRow(lines[1]);
            const rows = lines.slice(2).map(splitRow);

            const aligns = alignLine.map(a => {
                const left = a.startsWith(":");
                const right = a.endsWith(":");
                if (left && right) {
                    return "center";
                }
                if (right) {
                    return "right";
                }
                return "left";
            });

            const ths = header
                .map((h, i) => `<th style="text-align:${aligns[i] ?? "left"}">${h}</th>`)
                .join("");

            const trs = rows
                .filter(r => r.some(x => x.length))
                .map(r => {
                    const tds = header.map((_, i) => {
                        const cell = r[i] ?? "";
                        return `<td style="text-align:${aligns[i] ?? "left"}">${cell}</td>`;
                    }).join("");
                    return `<tr>${tds}</tr>`;
                })
                .join("");

            return `<div class="table-wrapper"><table><thead><tr>${ths}</tr></thead><tbody>${trs}</tbody></table></div>`;
        }

        // code fences ```lang\n...\n```
        let out = esc.replace(/```([a-zA-Z0-9_-]+)?\n([\s\S]*?)```/g, (_, lang, code) => {
            const prismLang = (lang || "").toLowerCase();
            const cls = prismLang ? ` class="language-${prismLang}"` : "";
            return `<pre><code${cls}>${code.replace(/\n$/, "")}</code></pre>`;
        });

        // inline code
        out = out.replace(/`([^`]+)`/g, (_, code) => `<code>${code}</code>`);

        // headings
        out = out
            .replace(/^###\s+(.*)$/gm, "<h3>$1</h3>")
            .replace(/^##\s+(.*)$/gm, "<h2>$1</h2>")
            .replace(/^#\s+(.*)$/gm, "<h1>$1</h1>");

        // bold / italics
        out = out
            .replace(/\*\*([^*]+)\*\*/g, "<strong>$1</strong>")
            .replace(/\*([^*]+)\*/g, "<em>$1</em>");

        // lists (- item)
        out = out.replace(/(?:^-\s+.*(?:\n|$))+?/gm, (block) => {
            const items = block.trim().split("\n")
                .map(l => l.replace(/^-+\s+/, "").trim())
                .filter(Boolean)
                .map(t => `<li>${t}</li>`)
                .join("");
            return `<ul>${items}</ul>\n`;
        });

        // tables
        {
            const lines = out.split("\n");
            const outLines = [];
            for (let i = 0; i < lines.length; i++) {
                const l0 = lines[i];
                const l1 = lines[i + 1];
                if (l0 && l1 && l0.includes("|") && isTableSep(l1)) {
                    const tableLines = [l0, l1];
                    i += 2;
                    while (i < lines.length && lines[i].includes("|") && lines[i].trim() !== "") {
                        tableLines.push(lines[i]);
                        i++;
                    }
                    i--;
                    outLines.push(renderTable(tableLines));
                    continue;
                }
                outLines.push(lines[i]);
            }
            out = outLines.join("\n");
        }

        // paragraphs
        const parts = out.split(/\n{2,}/).map(p => p.trim()).filter(Boolean);
        out = parts.map(p => {
            if (/^(<h\d|<pre|<ul|<ol|<table|<div class="table-wrapper"|<blockquote)/.test(p)) {
                return p;
            }
            return `<p>${p.replace(/\n/g, "<br>")}</p>`;
        }).join("\n");

        return out;
    }

    function addMessage(role, text, id) {
        const el = document.createElement("div");
        el.className = "msg";
        if (id) {
            el.dataset.id = id;
        }

        el.innerHTML = `
      <div class="meta">
        <span class="role">${escapeHtml(role)}</span>
        <span class="time">${escapeHtml(now())}</span>
      </div>
      <div class="content">${renderMarkdown(text ?? "")}</div>
    `;

        log.appendChild(el);
        log.scrollTop = log.scrollHeight;
        highlightUnder(el);
        return el;
    }

    function highlightUnder(rootEl) {
        // Prism is loaded via <script> tags in HTML; highlight only in this message
        if (window.Prism && typeof window.Prism.highlightAllUnder === "function") {
            window.Prism.highlightAllUnder(rootEl);
        }
    }

    function setMessageHtmlById(id, mdText) {
        const content = log.querySelector(`.msg[data-id="${CSS.escape(id)}"] .content`);
        if (!content) {
            return;
        }
        content.innerHTML = renderMarkdown(mdText ?? "");
        log.scrollTop = log.scrollHeight;
        highlightUnder(content);
    }

    function buildAskRequest() {
        const q = (questionEl.value || "").trim();
        const topK = Number(topKEl.value || "10");

        const reposCsv = (reposCsvEl.value || "").trim() || null;
        const explicitRepo = (explicitRepoEl.value || "").trim();
        const explicitRepoNames = explicitRepo ? [explicitRepo] : [];

        return {
            currentDirectory: null,
            question: q,
            topK: Number.isFinite(topK) ? topK : 10,
            explicitRepoNames,
            reposCsv,
            projectName: null,
            clientName: null,
            noLlm: !!noLlmEl.checked,
            verbose: !!verboseEl.checked,
            allRepos: !!allReposEl.checked,
            useAdvancedModel: !!useAdvancedModelEl.checked,
        };
    }

    function send() {
        const req = buildAskRequest();
        if (!req.question) {
            return;
        }

        const answerId = `ans_${Date.now()}`;
        answerBuffers.set(answerId, "");

        addMessage("You", req.question);
        addMessage("Minorag", "", answerId);

        setStatus("Asking…");
        sendBtn.disabled = true;

        vscode.postMessage({ type: "ask", request: req, answerId });
    }

    sendBtn.addEventListener("click", send);
    clearBtn.addEventListener("click", () => {
        log.innerHTML = "";
        answerBuffers.clear();
        setStatus("Ready");
    });

    questionEl.addEventListener("keydown", (e) => {
        if (e.key === "Enter" && !e.shiftKey) {
            e.preventDefault();
            send();
        }
    });

    window.addEventListener("message", (event) => {
        const msg = event.data;
        if (!msg || !msg.type) {
            return;

        }

        if (msg.type === "baseUrl") {
            return void setBaseUrl(msg.text);

        }
        if (msg.type === "status") {
            return void setStatus(msg.text);
        }

        if (msg.type === "askStart") {
            setStatus("Streaming…");
            if (!answerBuffers.has(msg.answerId)) {
                answerBuffers.set(msg.answerId, "");
            }
            return;
        }

        if (msg.type === "askChunk") {
            const id = msg.answerId;
            const prev = answerBuffers.get(id) ?? "";
            const next = prev + String(msg.text ?? "");
            answerBuffers.set(id, next);
            setMessageHtmlById(id, next);
            return;
        }

        if (msg.type === "askDone") {
            sendBtn.disabled = false;
            setStatus("Ready");
            questionEl.value = "";
            questionEl.focus();
            return;
        }

        if (msg.type === "askError") {
            sendBtn.disabled = false;
            setStatus("Failed");
            const id = msg.answerId;
            const text = msg.text || "Request failed";
            const prev = answerBuffers.get(id) ?? "";
            const errMd = prev ? `${prev}\n\n**Error:** ${text}` : `**Error:** ${text}`;
            answerBuffers.set(id, errMd);
            setMessageHtmlById(id, errMd);
            return;
        }
    });
})();