import { CommonModule } from '@angular/common';
import { Component, computed, effect, inject, output, signal } from '@angular/core';
import { ClientDto, MinoragApi, ProjectDto, RepoDto } from '../../api/minorag-api';
import { ChatParams } from '../chat-params';
import { RepoMultiSelectComponent } from '../repo-multi-select/repo-multi-select';

@Component({
  selector: 'app-chat-settings',
  standalone: true,
  imports: [CommonModule, RepoMultiSelectComponent],
  templateUrl: './chat-settings.html',
  styleUrls: ['./chat-settings.scss'],
})
export class ChatSettings {
  private api = inject(MinoragApi);

  paramsChange = output<ChatParams>();

  clients = signal<ClientDto[]>([]);
  projects = signal<ProjectDto[]>([]);
  repos = signal<RepoDto[]>([]);

  clientId = signal<number | null>(null);
  projectId = signal<number | null>(null);
  explicitRepoIds = signal<number[]>([]);

  topK = signal(18);
  noLlm = signal(false);
  verbose = signal(false);
  allRepos = signal(true);
  useAdvancedModel = signal(false);

  loading = signal(false);
  error = signal<string | null>(null);

  // If any scope is selected, allRepos must be false and toggle must be disabled
  hasScopeSelection = computed(() => {
    const cid = this.clientId();
    const pid = this.projectId();
    const repoCount = this.explicitRepoIds().length;
    return cid != null || pid != null || repoCount > 0;
  });

  allReposDisabled = computed(() => this.hasScopeSelection());

  filteredProjects = computed(() => {
    const cid = this.clientId();
    return cid == null ? [] : this.projects().filter((p) => p.clientId === cid);
  });

  filteredRepos = computed(() => {
    const cid = this.clientId();
    const pid = this.projectId();
    return this.repos().filter(
      (r) => (cid == null || r.clientId === cid) && (pid == null || r.projectId === pid)
    );
  });

  // ---- guards / last-known selections ----
  private clientsInFlight = false;
  private projectsKeyInFlight: string | null = null;
  private reposKeyInFlight: string | null = null;

  private lastClientId: number | null = null;
  private lastProjectId: number | null = null;

  constructor() {
    effect(() => {
      void this.loadClientsOnce();
    });

    effect(() => {
      const cid = this.clientId();

      if (cid === this.lastClientId) return;
      this.lastClientId = cid;

      this.lastProjectId = null;

      if (cid == null) {
        this.projects.set([]);
        this.repos.set([]);
        this.projectId.set(null);
        this.explicitRepoIds.set([]);

        this.emitParams();
        return;
      }

      this.projectId.set(null);
      this.explicitRepoIds.set([]);

      void this.loadProjectsForClient(cid);
      void this.loadReposForScope(cid, null);

      this.emitParams();
    });

    effect(() => {
      const cid = this.clientId();
      const pid = this.projectId();

      if (cid == null) return;

      if (pid === this.lastProjectId) return;
      this.lastProjectId = pid;

      this.explicitRepoIds.set([]);
      void this.loadReposForScope(cid, pid);

      this.emitParams();
    });

    // ✅ enforce allRepos rule
    effect(() => {
      const scoped = this.hasScopeSelection();
      if (scoped) {
        if (this.allRepos()) this.allRepos.set(false);
      } else {
        // optional UX: when no scope is selected, default back to allRepos=true
        if (!this.allRepos()) this.allRepos.set(true);
      }
    });

    effect(() => {
      this.topK();
      this.noLlm();
      this.verbose();
      this.allRepos();
      this.useAdvancedModel();
      this.explicitRepoIds();
      this.clientId();
      this.projectId();
      this.emitParams();
    });
  }

  onClientSelectChange(e: Event) {
    const v = (e.target as HTMLSelectElement).value;

    if (!v) {
      this.clientId.set(null);
      this.projectId.set(null);
      this.explicitRepoIds.set([]);
      return;
    }

    const id = Number(v);
    this.clientId.set(Number.isFinite(id) ? id : null);
  }

  onProjectSelectChange(e: Event) {
    const v = (e.target as HTMLSelectElement).value;

    if (!v) {
      this.projectId.set(null);
      this.explicitRepoIds.set([]);
      return;
    }

    const id = Number(v);
    this.projectId.set(Number.isFinite(id) ? id : null);
  }

  onTopKInput(e: Event) {
    const v = (e.target as HTMLInputElement).value;
    const n = Number(v);
    this.topK.set(Number.isFinite(n) ? Math.max(1, Math.min(50, n)) : 8);
  }

  onToggleAllRepos(e: Event) {
    // if disabled, ignore any event
    if (this.allReposDisabled()) return;
    this.allRepos.set((e.target as HTMLInputElement).checked);
  }

  onToggleNoLlm(e: Event) {
    this.noLlm.set((e.target as HTMLInputElement).checked);
  }

  onToggleVerbose(e: Event) {
    this.verbose.set((e.target as HTMLInputElement).checked);
  }

  onToggleAdvanced(e: Event) {
    this.useAdvancedModel.set((e.target as HTMLInputElement).checked);
  }

  onRepoIdsChange(ids: number[]) {
    this.explicitRepoIds.set(ids);
  }

  private emitParams() {
    this.paramsChange.emit({
      topK: this.topK(),
      clientId: this.clientId(),
      projectId: this.projectId(),
      explicitRepoIds: this.explicitRepoIds(),
      noLlm: this.noLlm(),
      verbose: this.verbose(),
      allRepos: this.allRepos(),
      useAdvancedModel: this.useAdvancedModel(),
    });
  }

  // ---- guarded loaders ----

  private async loadClientsOnce() {
    if (this.clients().length) return;
    if (this.clientsInFlight) return;

    this.clientsInFlight = true;
    this.loading.set(true);
    this.error.set(null);

    try {
      const xs = await this.api.getClients();
      this.clients.set(xs);
    } catch (e: any) {
      this.error.set(e?.message ?? String(e));
    } finally {
      this.loading.set(false);
      this.clientsInFlight = false;
    }
  }

  private async loadProjectsForClient(clientId: number) {
    const key = `c:${clientId}`;
    if (this.projectsKeyInFlight === key) return;
    this.projectsKeyInFlight = key;

    this.loading.set(true);
    this.error.set(null);

    try {
      const xs = await this.api.getProjects([clientId]);
      this.projects.set(xs);
    } catch (e: any) {
      this.error.set(e?.message ?? String(e));
      this.projects.set([]);
    } finally {
      this.loading.set(false);
      if (this.projectsKeyInFlight === key) this.projectsKeyInFlight = null;
    }
  }

  private async loadReposForScope(clientId: number, projectId: number | null) {
    const key = `c:${clientId}|p:${projectId ?? 'all'}`;
    if (this.reposKeyInFlight === key) return;
    this.reposKeyInFlight = key;

    this.loading.set(true);
    this.error.set(null);

    try {
      const xs = await this.api.getRepos([clientId], projectId == null ? [] : [projectId]);
      this.repos.set(xs);
    } catch (e: any) {
      this.error.set(e?.message ?? String(e));
      this.repos.set([]);
    } finally {
      this.loading.set(false);
      if (this.reposKeyInFlight === key) this.reposKeyInFlight = null;
    }
  }
}
