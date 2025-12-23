import { Component, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MinoragApi } from '../api/minorag-api';

type Severity = 'ok' | 'info' | 'warn' | 'error';

interface DoctorViewItem {
  label: string;
  description?: string;
  hint?: string | null;
  severityNum?: number | null;
  severity: Severity;
  raw: any;
  expanded: boolean;
}

@Component({
  selector: 'app-doctor',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './doctor.html',
  styleUrls: ['./doctor.scss'],
})
export class Doctor {
  items = signal<DoctorViewItem[]>([]);
  running = signal(false);
  error = signal<string | null>(null);

  constructor(private api: MinoragApi) {}

  run() {
    if (this.running()) return;

    this.items.set([]);
    this.error.set(null);
    this.running.set(true);

    this.api
      .doctorStream((raw) => {
        const item = this.normalize(raw);
        if (item.label.trim().length == 0) {
          return;
        }
        this.items.update((xs) => [...xs, item]);
      })
      .catch((e) => this.error.set(e?.message ?? String(e)))
      .finally(() => this.running.set(false));
  }

  toggle(i: number) {
    this.items.update((xs) =>
      xs.map((x, idx) => (idx === i ? { ...x, expanded: !x.expanded } : x))
    );
  }

  private normalize(raw: any): DoctorViewItem {
    const label = raw?.Label ?? raw?.label ?? raw?.Title ?? raw?.title ?? 'Doctor';

    const description = raw?.Description ?? raw?.description ?? '';

    const hint = raw?.Hint ?? raw?.hint ?? null;

    const sevNum: number | null =
      typeof raw?.Severity === 'number'
        ? raw.Severity
        : typeof raw?.severity === 'number'
        ? raw.severity
        : null;

    const severity: Severity =
      sevNum === 0
        ? 'warn'
        : sevNum === 1
        ? 'error'
        : sevNum === 2
        ? 'ok'
        : sevNum === 3
        ? 'info'
        : 'info';

    return {
      label: String(label),
      description: description ? String(description) : undefined,
      hint: hint == null ? null : String(hint),
      severityNum: sevNum,
      severity,
      raw,
      expanded: false,
    };
  }

  prettyJson(x: any): string {
    return JSON.stringify(x, null, 2);
  }
}
