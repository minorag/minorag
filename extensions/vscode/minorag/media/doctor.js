(function () {
    const vscode = acquireVsCodeApi();

    const rowsEl = document.getElementById("rows");
    const statusEl = document.getElementById("status");
    const baseUrlEl = document.getElementById("baseUrl");
    const runBtn = document.getElementById("run");
    const clearBtn = document.getElementById("clear");

    function escapeHtml(s) {
        return String(s).replace(/[&<>"']/g, (c) => ({
            "&": "&amp;",
            "<": "&lt;",
            ">": "&gt;",
            '"': "&quot;",
            "'": "&#39;",
        }[c]));
    }

    function addRow(r) {
        const tr = document.createElement("tr");

        if (r.isBlank) {
            tr.className = "blank";
            tr.innerHTML = "<td colspan='3'></td>";
            rowsEl.appendChild(tr);
            return;
        }

        if (r.isHeader) {
            tr.className = "header";
            tr.innerHTML = `
        <td></td>
        <td colspan="2">${escapeHtml(r.label ?? "")}</td>`;
            rowsEl.appendChild(tr);
            return;
        }

        const sev = r.severity ?? "Info";
        tr.innerHTML = `
      <td class="sev-${sev}">${escapeHtml(sev)}</td>
      <td>${escapeHtml(r.label ?? "")}</td>
      <td>
        <div>${escapeHtml(r.description ?? "")}</div>
        ${r.hint ? `<div class="hint">${escapeHtml(r.hint)}</div>` : ""}
      </td>`;
        rowsEl.appendChild(tr);
    }

    function clearRows() {
        rowsEl.innerHTML = "";
    }

    function setStatus(t) {
        statusEl.textContent = t ?? "";
    }

    function setBaseUrl(t) {
        baseUrlEl.textContent = t ?? "—";
    }

    runBtn.addEventListener("click", () => {
        runBtn.disabled = true;
        setStatus("Running…");
        clearRows();
        vscode.postMessage({ type: "runDoctor" });
    });

    clearBtn.addEventListener("click", () => {
        clearRows();
        setStatus("Ready");
    });

    window.addEventListener("message", (event) => {
        const msg = event.data;

        if (!msg || !msg.type) {
            return;

        }

        if (msg.type === "status") {
            setStatus(msg.text);
            return;
        }

        if (msg.type === "baseUrl") {
            setBaseUrl(msg.text);
            return;
        }

        if (msg.type === "clear") {
            clearRows();
            return;
        }

        if (msg.type === "row") {
            addRow(msg.row);
            return;
        }

        if (msg.type === "done") {
            runBtn.disabled = false;
            if (msg.text) {
                setStatus(msg.text);
            }
            return;
        }

        if (msg.type === "error") {
            runBtn.disabled = false;
            setStatus(msg.text || "Error");
            return;
        }
    });
})();