import { CommonModule } from '@angular/common';
import { Component, input, output } from '@angular/core';
import { RepoDto } from '../../api/minorag-api';

@Component({
  selector: 'app-repo-multiselect',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './repo-multi-select.html',
  styleUrls: ['./repo-multi-select.scss'],
})
export class RepoMultiSelectComponent {
  repos = input.required<RepoDto[]>();
  selectedIds = input.required<number[]>();

  selectedIdsChange = output<number[]>();

  onCheckboxChange(id: number, e: Event) {
    const checked = (e.target as HTMLInputElement).checked;
    this.toggle(id, checked);
  }

  toggle(id: number, checked: boolean) {
    const set = new Set(this.selectedIds());
    checked ? set.add(id) : set.delete(id);
    this.selectedIdsChange.emit([...set]);
  }

  clear() {
    this.selectedIdsChange.emit([]);
  }

  all() {
    this.selectedIdsChange.emit(this.repos().map((r) => r.id));
  }

  isSelected(id: number) {
    return this.selectedIds().includes(id);
  }
}
