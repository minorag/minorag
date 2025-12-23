import { Injectable } from '@angular/core';
import { environment } from '../../environments/environment';

export interface ClientDto {
  id: number;
  name: string;
}

export interface ProjectDto {
  id: number;
  name: string;
  clientId: number;
}

export interface RepoDto {
  id: number;
  name: string;
  clientId: number;
  projectId: number;
}

export interface AskRequest {
  currentDirectory: string;
  question: string;
  topK: number;
  explicitRepoNames: string[];
  reposCsv: string;
  projectName: string;
  clientName: string;
  noLlm: boolean;
  verbose: boolean;
  allRepos: boolean;
  useAdvancedModel: boolean;
}

@Injectable({ providedIn: 'root' })
export class MinoragApi {
  private readonly base = environment.apiBaseUrl;

  // ---- request dedupe + caching ----
  private inflight = new Map<string, Promise<any>>();
  private cache = new Map<string, any>();

  private async fetchText(
    url: string,
    init?: RequestInit
  ): Promise<{ ok: boolean; status: number; text: string }> {
    const res = await fetch(url, init);
    const text = await res.text();
    return { ok: res.ok, status: res.status, text };
  }

  private isHtml(text: string): boolean {
    const t = text.trimStart();
    return t.startsWith('<!DOCTYPE') || t.startsWith('<html') || t.startsWith('<');
  }

  private async getJsonCached<T>(key: string, url: string): Promise<T> {
    if (this.cache.has(key)) return this.cache.get(key) as T;
    if (this.inflight.has(key)) return (await this.inflight.get(key)) as T;

    const p = (async () => {
      const { ok, status, text } = await this.fetchText(url);

      if (!ok) {
        throw new Error(`GET ${url} failed: ${status}\n${text.slice(0, 800)}`);
      }

      if (this.isHtml(text)) {
        throw new Error(
          `GET ${url} returned HTML (SPA fallback/proxy issue). First chars:\n${text.slice(0, 200)}`
        );
      }

      const data = JSON.parse(text) as T;
      this.cache.set(key, data);
      return data;
    })();

    this.inflight.set(key, p);
    try {
      return await p;
    } finally {
      this.inflight.delete(key);
    }
  }

  // Optional: call when you know DB scope changed and you want fresh lists
  clearCache() {
    this.cache.clear();
  }

  // ---- API calls ----

  async askStream(req: AskRequest, onChunk: (chunk: string) => void): Promise<void> {
    const res = await fetch(`${this.base}/ask`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(req),
    });

    if (!res.ok) {
      const text = await res.text();
      throw new Error(`POST ${this.base}/ask failed: ${res.status}\n${text.slice(0, 800)}`);
    }

    if (!res.body) {
      onChunk(await res.text());
      return;
    }

    const reader = res.body.getReader();
    const decoder = new TextDecoder('utf-8');

    while (true) {
      const { value, done } = await reader.read();
      if (done) break;
      onChunk(decoder.decode(value, { stream: true }));
    }
  }

  async doctorStream(onItem: (item: any) => void): Promise<void> {
    const res = await fetch(`${this.base}/doctor`, {
      method: 'PATCH',
      headers: { Accept: 'application/x-ndjson' },
    });

    if (!res.ok) {
      const text = await res.text();
      throw new Error(`PATCH ${this.base}/doctor failed: ${res.status}\n${text.slice(0, 800)}`);
    }

    if (!res.body) {
      const text = await res.text();
      text
        .split('\n')
        .filter(Boolean)
        .forEach((line) => onItem(JSON.parse(line)));
      return;
    }

    const reader = res.body.getReader();
    const decoder = new TextDecoder('utf-8');
    let buffer = '';

    while (true) {
      const { value, done } = await reader.read();
      if (done) break;

      buffer += decoder.decode(value, { stream: true });

      let idx: number;
      while ((idx = buffer.indexOf('\n')) >= 0) {
        const line = buffer.slice(0, idx).trim();
        buffer = buffer.slice(idx + 1);

        if (!line) continue;
        try {
          onItem(JSON.parse(line));
        } catch {
          // ignore partial/invalid lines
        }
      }
    }

    const tail = buffer.trim();
    if (tail) onItem(JSON.parse(tail));
  }

  async getClients(): Promise<ClientDto[]> {
    // stable list -> cache strongly
    return this.getJsonCached<ClientDto[]>('clients', `${this.base}/clients`);
  }

  async getProjects(clientIds: number[]): Promise<ProjectDto[]> {
    const ids = [...clientIds].filter((x) => Number.isFinite(x)).sort((a, b) => a - b);
    const qs = ids.map((x) => `clientIds=${encodeURIComponent(x)}`).join('&');
    const key = `projects:${ids.join(',')}`;
    return this.getJsonCached<ProjectDto[]>(key, `${this.base}/projects?${qs}`);
  }

  async getRepos(clientIds: number[] = [], projectIds: number[] = []): Promise<RepoDto[]> {
    const cIds = [...clientIds].filter((x) => Number.isFinite(x)).sort((a, b) => a - b);
    const pIds = [...projectIds].filter((x) => Number.isFinite(x)).sort((a, b) => a - b);

    const parts: string[] = [];
    for (const id of cIds) parts.push(`clientIds=${encodeURIComponent(id)}`);
    for (const id of pIds) parts.push(`projectIds=${encodeURIComponent(id)}`);

    const qs = parts.join('&');
    const url = `${this.base}${qs ? `/repos?${qs}` : `/repos`}`;
    const key = `repos:c=${cIds.join(',')};p=${pIds.join(',')}`;

    return this.getJsonCached<RepoDto[]>(key, url);
  }
}
