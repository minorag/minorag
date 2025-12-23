import { Component, computed, effect, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MinoragApi, AskRequest, ClientDto, ProjectDto, RepoDto } from '../api/minorag-api';
import { MarkdownService } from '../shared/markdown.service';
import { ChatSettings } from './chat-settings/chat-settings';
import { ChatParams } from './chat-params';

type Role = 'user' | 'assistant';
interface ChatMessage {
  role: Role;
  content: string;
}

declare const acquireVsCodeApi: () => any;

@Component({
  selector: 'app-chat',
  standalone: true,
  imports: [CommonModule, ChatSettings],
  templateUrl: './chat.html',
  styleUrls: ['./chat.scss'],
})
export class Chat {
  private vscode = typeof acquireVsCodeApi === 'function' ? acquireVsCodeApi() : null;

  private api = inject(MinoragApi);
  private md = inject(MarkdownService);

  settingsOpen = signal(false);

  messages = signal<ChatMessage[]>([]);
  input = signal('');
  busy = signal(false);
  settingsSummary = computed(() => {
    const p = this.params();

    const scope: string[] = [];

    if (p.allRepos) {
      scope.push('all repos');
    } else {
      const cn = this.clientName();
      const pn = this.projectName();

      if (cn) {
        scope.push(cn);
      }
      if (pn) {
        scope.push(pn);
      }

      const repoNames = this.explicitRepoNames();
      if (repoNames.length) {
        const max = 3; // show first 3, then "+N"
        const shown = repoNames.slice(0, max);
        const rest = repoNames.length - shown.length;

        scope.push(`repos: ${shown.join(', ')}${rest > 0 ? ` +${rest}` : ''}`);
      }

      // if nothing selected and not allRepos, show fallback
      if (!cn && !pn && !p.explicitRepoIds.length) {
        scope.push('scope: (none)');
      }
    }

    const flags: string[] = [];
    if (p.noLlm) flags.push('no-llm');
    if (p.verbose) flags.push('verbose');
    if (p.useAdvancedModel) flags.push('advanced');

    const flagsText = flags.length ? ` · ${flags.join(' · ')}` : '';
    return `TopK ${p.topK} · ${scope.join(' · ')}${flagsText}`;
  });

  params = signal<ChatParams>({
    topK: 18,
    clientId: null,
    projectId: null,
    explicitRepoIds: [],
    noLlm: false,
    verbose: false,
    allRepos: true,
    useAdvancedModel: false,
  });

  clients = signal<ClientDto[]>([]);
  projects = signal<ProjectDto[]>([]);
  repos = signal<RepoDto[]>([]);
  lookupError = signal<string | null>(null);

  clientName = computed(() => {
    const cid = this.params().clientId;
    if (cid == null) return '';
    return this.clients().find((c) => c.id === cid)?.name ?? '';
  });

  projectName = computed(() => {
    const pid = this.params().projectId;
    if (pid == null) return '';
    return this.projects().find((p) => p.id === pid)?.name ?? '';
  });

  explicitRepoNames = computed(() => {
    const ids = this.params().explicitRepoIds;
    if (!ids.length) return [];
    const byId = new Map(this.repos().map((r) => [r.id, r.name] as const));
    return ids.map((id) => byId.get(id)).filter((x): x is string => !!x);
  });

  constructor() {
    window.addEventListener('message', (event) => {
      const msg = event.data;
      if (msg?.type === 'answer') this.addAssistant(msg.text ?? '');
      if (msg?.type === 'answer-stream') this.appendAssistantChunk(msg.text ?? '');
    });

    effect(() => {
      const cid = this.params().clientId;
      const pid = this.params().projectId;

      // We can still load clients once
      this.ensureClientsLoaded();

      if (cid != null) {
        this.loadProjects(cid);
        this.loadRepos(cid, pid);
      }
    });
  }

  openSettings() {
    this.settingsOpen.set(true);
  }

  onParamsChange(p: ChatParams) {
    this.params.set(p);
  }

  onInput(event: Event) {
    this.input.set((event.target as HTMLInputElement).value);
  }

  render(text: string): string {
    return this.md.toSafeHtml(text);
  }

  closeSettings() {
    this.settingsOpen.set(false);
  }

  onKeyDownDialog(e: KeyboardEvent) {
    if (e.key === 'Escape') this.closeSettings();
  }

  async send() {
    const q = this.input().trim();
    if (!q || this.busy()) return;

    this.addUser(q);
    this.input.set('');

    const p = this.params();

    // VS Code mode: delegate to extension (include params so extension can call API)
    if (this.vscode) {
      this.vscode.postMessage({
        type: 'ask',
        text: q,
        params: p,
      });
      return;
    }

    const req: AskRequest = {
      currentDirectory: '/',
      question: q,
      topK: p.topK,
      clientName: this.clientName(),
      projectName: this.projectName(),
      explicitRepoNames: this.explicitRepoNames(),
      reposCsv: '',
      noLlm: p.noLlm,
      verbose: p.verbose,
      allRepos: p.allRepos,
      useAdvancedModel: p.useAdvancedModel,
    };

    this.busy.set(true);

    try {
      await this.api.askStream(req, (chunk) => this.appendAssistantChunk(chunk));
    } catch (err: any) {
      this.addAssistant(`Error calling /ask: ${err?.message ?? err}`);
    } finally {
      this.busy.set(false);
    }
  }

  // ---- lookup loading ----

  private async ensureClientsLoaded() {
    if (this.clients().length) return;
    this.lookupError.set(null);
    try {
      this.clients.set(await this.api.getClients());
    } catch (e: any) {
      this.lookupError.set(e?.message ?? String(e));
    }
  }

  private async loadProjects(clientId: number) {
    this.lookupError.set(null);
    try {
      this.projects.set(await this.api.getProjects([clientId]));
    } catch (e: any) {
      this.lookupError.set(e?.message ?? String(e));
      this.projects.set([]);
    }
  }

  private async loadRepos(clientId: number, projectId: number | null) {
    this.lookupError.set(null);
    try {
      this.repos.set(await this.api.getRepos([clientId], projectId == null ? [] : [projectId]));
    } catch (e: any) {
      this.lookupError.set(e?.message ?? String(e));
      this.repos.set([]);
    }
  }

  // ---- messages helpers ----

  private addUser(text: string) {
    this.messages.update((m) => [...m, { role: 'user', content: text }]);
  }

  private addAssistant(text: string) {
    this.messages.update((m) => [...m, { role: 'assistant', content: text }]);
  }

  private appendAssistantChunk(chunk: string) {
    if (!chunk) return;

    this.messages.update((m) => {
      const last = m[m.length - 1];
      if (last?.role === 'assistant') {
        return [...m.slice(0, -1), { role: 'assistant', content: last.content + chunk }];
      }
      return [...m, { role: 'assistant', content: chunk }];
    });
  }
}
